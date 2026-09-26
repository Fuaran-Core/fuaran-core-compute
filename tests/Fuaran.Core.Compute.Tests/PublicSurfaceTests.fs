module Fuaran.Core.Tests.PublicSurfaceTests

// ---------------------------------------------------------------------------
// Phase 183 — a committed public-surface baseline per package, and the CLASS of a move
// computed rather than argued.
//
// The draft-slot rule asks one question of every commit that touches a package: does its
// public contract move, and if so in which class. On 2026-09-15 that question was answered
// twice by hand, in two workers' deviations records — "additive, and it advances because the
// slot is tagged"; "additive, rides the draft". Both were right; neither was checked, and
// neither could be: this repository had no surface baseline at all, so the only instrument
// was a reading of the diff.
//
// So: `api/<package>.txt`, one committed file per packable package, rendered from the built
// assembly's IL METADATA — the same source a pack-time surface guard reads, and the only
// one that sees what a consumer actually links against. A `PublicSurface` test family renders
// each package afresh and diffs it against its baseline; the diff is CLASSIFIED, and the test
// fails unless the commit also moved the baseline.
//
// **What this deliberately is NOT: a gate on the class.** Additive or breaking, a classified
// move passes. What is refused is an UNCLASSIFIED one — a surface that moved with its baseline
// standing still. Widening is permitted, widening in silence is not; the class line is what
// lets a reviewer apply it.
//
// ---- the six classes, and why the three that LOOK additive are not -------------------
//
// A naive differ calls a removed token breaking and an added token additive. That reading is
// what let the two shapes below occupy unchanged feed slots, so it is not the reading here:
//
//   removal             a baseline token with no counterpart — removed or renamed. Call sites
//                       stop resolving.
//   retype              the same member, rendered differently: a parameter or return type
//                       changed, or a record field's POSITION moved. Paired by identity, so it
//                       is reported as one move rather than as a removal beside an addition.
//   record-widening     a record field ADDED to a record the baseline published. Every
//                       full-literal construction stops compiling (FS0764) and the primary
//                       constructor widens.
//   union-widening      a case ADDED to a union the baseline published. Every exhaustive
//                       `match` becomes incomplete — and a consumer holding a stale
//                       same-version pack gets an `InvalidCastException` at run time instead,
//                       with no compile signal at all.
//   interface-widening  a member ADDED to an interface the baseline published. Every
//                       implementer stops compiling.
//   additive            genuinely additive growth: a new type, a new module function, a new
//                       member on a class — and a field or case on a type the baseline never
//                       published, which nobody could have constructed or matched.
//
// The middle three are read off the `CompilationMappingAttribute` the F# compiler already
// emits, which is what makes them computable at all: a union case addition REMOVES NOTHING, so
// a removal-only differ sees ordinary growth.
//
// ---- what it does not claim ---------------------------------------------------------
//
//   * SEMANTICS. A function whose signature is unchanged and whose behaviour reversed is
//     invisible here and always will be.
//   * The Fable SOURCE half of a package. Every packable project here also ships its `.fs`
//     sources under `fable/` for a Fable consumer to compile; this renders the managed
//     assembly, which is the .NET consumer's contract. A source-only change that is
//     Fable-visible and managed-invisible is not a shape F# can produce, but the boundary is
//     named rather than assumed.
//   * INTERNAL members, which cannot break a consumer and are excluded from the surface.
//
// ---- regenerating ------------------------------------------------------------------
//
//   CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Compute.Tests
//
// The forge precedent, and its hazard is the forge one too: it rewrites EVERY baseline, not
// the one you were looking at, so an unrelated drift sitting in the tree lands in your commit
// silently. Stage the baselines you meant to move BY NAME and read the rest back out.
// ---------------------------------------------------------------------------

open System
open System.Collections.Immutable
open System.Diagnostics
open System.IO
open System.Reflection
open System.Reflection.Metadata
open System.Reflection.PortableExecutable
open System.Text
open Expecto

// ---- probe types: what the renderer is pinned against ---------------------
//
// Declared HERE, in the test assembly, so the renderer's output FORMAT has a case whose
// expected text is known in advance. Every other leg reads a real package's surface and
// compares it with a file generated by this same renderer — a pairing that would agree
// perfectly with itself if the renderer emitted nothing at all.

/// A record with two fields, in declaration order — the `record-field` token's shape and its
/// `#seq`.
type ProbeRecord = { Alpha: int; Beta: string }

/// A union with a nullary case and a carrying one — the `union-case` token's shape and `#tag`.
type ProbeUnion =
    | ProbeEmpty
    | ProbeCarrying of int * string

/// A single-case union with a NAMED field — its field property sits on the union type itself
/// and carries the two-argument `(Field, seq)` mapping (Phase 237).
type ProbeSingle = ProbeSingle of value: int

/// A struct union — its fields sit on the union type itself too, each carrying
/// `(Field, variant, seq)` (Phase 237).
[<Struct>]
type ProbeStructUnion =
    | ProbeStructA of a: int
    | ProbeStructB of b: string * c: int

/// Phase 237's go-red fixture pair, and its control. Three modules, each declaring the same
/// union; `Before` and `After` differ ONLY by one field's name, `Same` by nothing. Rendered from
/// this assembly's own IL, so the pair is a real compiled pair rather than two edited strings.
module ProbeFieldsBefore =
    type Shape =
        | Empty
        | Held of parent: int * kindTag: string

module ProbeFieldsAfter =
    type Shape =
        | Empty
        | Held of target: int * kindTag: string

module ProbeFieldsSame =
    type Shape =
        | Empty
        | Held of parent: int * kindTag: string

/// An interface — the `interface-marker` token, which is what lets the classifier call a
/// member added to a published interface breaking without re-reading the assembly.
type IProbeSeam =
    abstract Probe: int -> string

// ---- the signature type provider ------------------------------------------
//
// `MetadataReader` hands signatures back as blobs; `DecodeSignature` walks one given a
// provider that names each type it meets. Rendering to STRING rather than to a reflection
// type is what keeps this resolution-free — a parameter typed from another assembly is named
// from its TypeReference row, with no need for that assembly to be loadable. That matters:
// several packable projects are not referenced by this test project at all, and
// `MetadataLoadContext` would need their whole dependency closure resolvable on disk.

type private SurfaceTypeProvider() =

    member private this.DefinitionName(r: MetadataReader, h: TypeDefinitionHandle) : string =
        let td = r.GetTypeDefinition h
        let name = r.GetString td.Name

        if td.IsNested then
            this.DefinitionName(r, td.GetDeclaringType()) + "+" + name
        else
            let ns = r.GetString td.Namespace
            if String.IsNullOrEmpty ns then name else ns + "." + name

    member private this.ReferenceName(r: MetadataReader, h: TypeReferenceHandle) : string =
        let tr = r.GetTypeReference h
        let name = r.GetString tr.Name

        if tr.ResolutionScope.Kind = HandleKind.TypeReference then
            let outer = TypeReferenceHandle.op_Explicit tr.ResolutionScope
            this.ReferenceName(r, outer) + "+" + name
        else
            let ns = r.GetString tr.Namespace
            if String.IsNullOrEmpty ns then name else ns + "." + name

    interface ISZArrayTypeProvider<string> with
        member _.GetSZArrayType(elementType) = elementType + "[]"

    interface ISimpleTypeProvider<string> with
        member _.GetPrimitiveType(code) = "System." + string code

        member this.GetTypeFromDefinition(r, handle, _rawTypeKind) = this.DefinitionName(r, handle)

        member this.GetTypeFromReference(r, handle, _rawTypeKind) = this.ReferenceName(r, handle)

    interface IConstructedTypeProvider<string> with
        member _.GetGenericInstantiation(genericType, typeArguments) =
            genericType + "<" + String.Join(", ", typeArguments) + ">"

        member _.GetArrayType(elementType, shape) =
            elementType + "[" + String.replicate (max 0 (shape.Rank - 1)) "," + "]"

        member _.GetByReferenceType(elementType) = elementType + "&"
        member _.GetPointerType(elementType) = elementType + "*"

    interface ISignatureTypeProvider<string, obj> with
        member _.GetFunctionPointerType(_signature) = "System.IntPtr(fnptr)"
        member _.GetGenericMethodParameter(_genericContext, index) = "!!" + string index
        member _.GetGenericTypeParameter(_genericContext, index) = "!" + string index

        member _.GetModifiedType(_modifier, unmodifiedType, _isRequired) = unmodifiedType

        member _.GetPinnedType(elementType) = elementType + " pinned"

        member this.GetTypeFromSpecification(r, genericContext, handle, _rawTypeKind) =
            let ts = r.GetTypeSpecification handle
            ts.DecodeSignature(this :> ISignatureTypeProvider<string, obj>, genericContext)

// ---- attribute decoding ---------------------------------------------------

/// The F# compiler stamps `CompilationMappingAttribute` on every construct it lowers, and all
/// three overloads take nothing but `Int32`s — `(flags)`, `(flags, seq)`,
/// `(flags, variant, seq)`. A custom-attribute blob for an Int32-only constructor has a fixed
/// layout (2-byte prolog, the fixed args, a 2-byte named-argument count), so it decodes
/// without the constructor's own signature. `None` when the blob is not that shape.
let private mappingInts (r: MetadataReader) (attrs: CustomAttributeHandleCollection) : int list option =
    let attributeName (ca: CustomAttribute) : string =
        try
            let ctor = ca.Constructor

            if ctor.Kind = HandleKind.MemberReference then
                let mr = r.GetMemberReference(MemberReferenceHandle.op_Explicit ctor)
                let parent = mr.Parent

                if parent.Kind = HandleKind.TypeReference then
                    r.GetString((r.GetTypeReference(TypeReferenceHandle.op_Explicit parent)).Name)
                elif parent.Kind = HandleKind.TypeDefinition then
                    r.GetString((r.GetTypeDefinition(TypeDefinitionHandle.op_Explicit parent)).Name)
                else
                    ""
            elif ctor.Kind = HandleKind.MethodDefinition then
                let md = r.GetMethodDefinition(MethodDefinitionHandle.op_Explicit ctor)
                r.GetString((r.GetTypeDefinition(md.GetDeclaringType())).Name)
            else
                ""
        with _ ->
            ""

    attrs
    |> Seq.tryPick (fun ah ->
        try
            let ca = r.GetCustomAttribute ah

            if attributeName ca <> "CompilationMappingAttribute" then
                None
            else
                let bytes = r.GetBlobBytes ca.Value

                if bytes.Length < 8 || bytes[0] <> 1uy || bytes[1] <> 0uy then
                    None
                else
                    let count = (bytes.Length - 4) / 4

                    if count < 1 then
                        None
                    else
                        Some [ for i in 0 .. count - 1 -> BitConverter.ToInt32(bytes, 2 + (i * 4)) ]
        with _ ->
            None)

let private hasAttribute (r: MetadataReader) (attrs: CustomAttributeHandleCollection) (name: string) : bool =
    let attributeName (ca: CustomAttribute) : string =
        try
            let ctor = ca.Constructor

            if ctor.Kind = HandleKind.MemberReference then
                let mr = r.GetMemberReference(MemberReferenceHandle.op_Explicit ctor)
                let parent = mr.Parent

                if parent.Kind = HandleKind.TypeReference then
                    r.GetString((r.GetTypeReference(TypeReferenceHandle.op_Explicit parent)).Name)
                elif parent.Kind = HandleKind.TypeDefinition then
                    r.GetString((r.GetTypeDefinition(TypeDefinitionHandle.op_Explicit parent)).Name)
                else
                    ""
            elif ctor.Kind = HandleKind.MethodDefinition then
                let md = r.GetMethodDefinition(MethodDefinitionHandle.op_Explicit ctor)
                r.GetString((r.GetTypeDefinition(md.GetDeclaringType())).Name)
            else
                ""
        with _ ->
            ""

    attrs
    |> Seq.exists (fun ah ->
        try
            attributeName (r.GetCustomAttribute ah) = name
        with _ ->
            false)

// ---- the renderer ---------------------------------------------------------

[<Literal>]
let private SourceConstructMask = 0x1F

[<Literal>]
let private SourceConstructSumType = 1

[<Literal>]
let private SourceConstructRecordType = 2

[<Literal>]
let private SourceConstructField = 4

[<Literal>]
let private SourceConstructModule = 7

[<Literal>]
let private SourceConstructUnionCase = 8

let private visibleMethodAccess =
    set
        [ MethodAttributes.Public
          MethodAttributes.Family
          MethodAttributes.FamORAssem ]

/// The rendered public contract surface of one managed assembly, ordinal-sorted.
///
/// "Public surface" is what a consumer in ANOTHER assembly can reach: public and protected
/// members of externally-visible types. Internal members are excluded — they cannot break a
/// consumer. Compiler-generated members are excluded too, with ONE deliberate carve-out: a DU
/// case factory carries BOTH `CompilerGeneratedAttribute` and
/// `CompilationMappingAttribute(UnionCase)`, and dropping it would discard the one signal the
/// union-widening class turns on.
let internal renderAssembly (dllPath: string) : string list =
    let provider = SurfaceTypeProvider()
    let sigProvider = provider :> ISignatureTypeProvider<string, obj>
    let tokens = ResizeArray<string>()

    use stream = File.OpenRead dllPath
    use pe = new PEReader(stream)

    if not pe.HasMetadata then
        []
    else
        let r = pe.GetMetadataReader()

        // Full name with '+' between nesting levels, matching the provider's rendering, so a
        // type named in a signature and the same type's own header agree.
        let rec fullName (th: TypeDefinitionHandle) : string =
            let td = r.GetTypeDefinition th
            let name = r.GetString td.Name

            if td.IsNested then
                fullName (td.GetDeclaringType()) + "+" + name
            else
                let ns = r.GetString td.Namespace
                if String.IsNullOrEmpty ns then name else ns + "." + name

        // Externally visible = public at EVERY level of nesting. A public type nested in an
        // internal one is unreachable and must not be rendered.
        let rec visibleType (th: TypeDefinitionHandle) : bool =
            let td = r.GetTypeDefinition th
            let vis = td.Attributes &&& TypeAttributes.VisibilityMask

            if td.IsNested then
                (vis = TypeAttributes.NestedPublic
                 || vis = TypeAttributes.NestedFamily
                 || vis = TypeAttributes.NestedFamORAssem)
                && visibleType (td.GetDeclaringType())
            else
                vis = TypeAttributes.Public

        // Phase 237 — the FIELD NAMES of every union case, keyed by (union, tag).
        //
        // A case's factory signature carries its field TYPES only, and its parameter names
        // are a lossy spelling of the field names (`target` is emitted as `_target`), so they
        // are not read. The names come from the properties the compiler emits for each field,
        // which carry `CompilationMappingAttribute(Field, variant, seq)`: on the case's nested
        // class for a multi-case reference union, on the union type itself for a single-case
        // or a struct union. The (Field, seq) two-argument form is the single-case shape, and
        // its variant is 0. What the renderer does with a case whose fields it could not name
        // is the conservative thing — it renders the types alone — and the guard test
        // "every carrying union case renders its field names" turns that into a failure.
        let caseFieldNames =
            Collections.Generic.Dictionary<struct (string * int), ResizeArray<int * string>>()

        let isUnion (th: TypeDefinitionHandle) =
            match mappingInts r ((r.GetTypeDefinition th).GetCustomAttributes()) with
            | Some(flags :: _) -> (flags &&& SourceConstructMask) = SourceConstructSumType
            | _ -> false

        for th in r.TypeDefinitions do
            let td = r.GetTypeDefinition th

            let unionOwner =
                if isUnion th then
                    Some th
                elif td.IsNested && isUnion (td.GetDeclaringType()) then
                    Some(td.GetDeclaringType())
                else
                    None

            match unionOwner with
            | None -> ()
            | Some u ->
                for ph in td.GetProperties() do
                    let pd = r.GetPropertyDefinition ph

                    match mappingInts r (pd.GetCustomAttributes()) with
                    | Some(flags :: rest) when (flags &&& SourceConstructMask) = SourceConstructField ->
                        let variant, seq =
                            match rest with
                            | [ v; s ] -> v, s
                            | [ s ] -> 0, s
                            | _ -> -1, -1

                        if variant >= 0 then
                            let key = struct (fullName u, variant)

                            match caseFieldNames.TryGetValue key with
                            | true, xs -> xs.Add((seq, r.GetString pd.Name))
                            | _ -> caseFieldNames[key] <- ResizeArray [ (seq, r.GetString pd.Name) ]
                    | _ -> ()

        /// The factory's parameter list, each type prefixed with its field name when the
        /// case's fields were all named — `target: !0, kindTag: System.String`.
        ///
        /// Measured, not assumed: a multi-case union's field is reached TWICE by the pass
        /// above, once on the case class and once on the union type, both rows naming the same
        /// (variant, seq). Hence `distinct` — and a genuine disagreement (two names at one
        /// position) still fails the count below and falls back, which the guard test catches.
        let caseParameters (union: string) (tag: int) (types: ImmutableArray<string>) : string =
            let named =
                match caseFieldNames.TryGetValue(struct (union, tag)) with
                | true, xs ->
                    let names = xs |> Seq.distinct |> Seq.sortBy fst |> Seq.map snd |> List.ofSeq

                    if names.Length = types.Length then
                        Some(List.map2 (fun n t -> n + ": " + t) names (List.ofSeq types))
                    else
                        None
                | _ -> None

            match named with
            | Some ps -> String.Join(", ", ps)
            | None -> String.Join(", ", types)

        for th in r.TypeDefinitions do
            let td = r.GetTypeDefinition th

            // Closures, startup classes and other lowered artefacts. F# spells them with '@'
            // or '<'; neither can occur in a name a consumer writes.
            let full = fullName th

            if
                visibleType th
                && not (full.Contains '@')
                && not (full.Contains '<')
                && not (hasAttribute r (td.GetCustomAttributes()) "CompilerGeneratedAttribute")
            then
                let construct =
                    match mappingInts r (td.GetCustomAttributes()) with
                    | Some(flags :: _) -> flags &&& SourceConstructMask
                    | _ -> -1

                let isInterface = td.Attributes.HasFlag TypeAttributes.Interface

                let kind =
                    if construct = SourceConstructRecordType then "record"
                    elif construct = SourceConstructSumType then "union"
                    elif construct = SourceConstructModule then "module"
                    elif isInterface then "interface"
                    else "type"

                tokens.Add(sprintf "type %s (%s)" full kind)

                // -- methods, constructors and DU case factories --
                for mh in td.GetMethods() do
                    try
                        let md = r.GetMethodDefinition mh
                        let access = md.Attributes &&& MethodAttributes.MemberAccessMask

                        if visibleMethodAccess.Contains access then
                            let name = r.GetString md.Name
                            let attrs = md.GetCustomAttributes()
                            let map = mappingInts r attrs

                            let isUnionCase =
                                match map with
                                | Some(flags :: _) -> (flags &&& SourceConstructMask) = SourceConstructUnionCase
                                | _ -> false

                            let skip =
                                not isUnionCase
                                && (hasAttribute r attrs "CompilerGeneratedAttribute"
                                    || (md.Attributes.HasFlag MethodAttributes.SpecialName
                                        && (name.StartsWith("get_", StringComparison.Ordinal)
                                            || name.StartsWith("set_", StringComparison.Ordinal)
                                            || name.StartsWith("add_", StringComparison.Ordinal)
                                            || name.StartsWith("remove_", StringComparison.Ordinal))))

                            if not skip then
                                let signature = md.DecodeSignature(sigProvider, null)
                                let ps = String.Join(", ", signature.ParameterTypes)

                                let generic =
                                    if signature.GenericParameterCount > 0 then
                                        "`" + string signature.GenericParameterCount
                                    else
                                        ""

                                if isUnionCase then
                                    let tag =
                                        match map with
                                        | Some ints when ints.Length >= 2 -> List.last ints
                                        | _ -> 0

                                    // A NULLARY case is emitted as a static PROPERTY whose
                                    // GETTER carries the mapping attribute — the property
                                    // itself carries only `CompilerGenerated`, so the getter
                                    // is the only row that names the case at all. Strip the
                                    // accessor prefix so the token names the CASE: no F#
                                    // union case can be spelled `get_…` (a case name must
                                    // start upper-case), so this can never shadow a real one.
                                    let caseName =
                                        if name.StartsWith("get_", StringComparison.Ordinal) then
                                            name.Substring 4
                                        else
                                            name

                                    tokens.Add(
                                        sprintf
                                            "union-case %s.%s #%d(%s)"
                                            full
                                            caseName
                                            tag
                                            (caseParameters full tag signature.ParameterTypes)
                                    )
                                elif name = ".ctor" then
                                    tokens.Add(sprintf "ctor %s..ctor(%s)" full ps)
                                else
                                    tokens.Add(
                                        sprintf "method %s.%s%s(%s) : %s" full name generic ps signature.ReturnType
                                    )
                    with e ->
                        // A member this reader cannot decode is skipped rather than fatal:
                        // an unreadable row must not take the whole baseline down with it.
                        // It surfaces as a MISSING token at the next diff, which is the
                        // conservative direction.
                        eprintfn "surface: method skipped in %s — %s" full e.Message

                // -- properties: record fields, nullary DU cases, ordinary properties --
                for ph in td.GetProperties() do
                    try
                        let pd = r.GetPropertyDefinition ph
                        let name = r.GetString pd.Name
                        let accessors = pd.GetAccessors()

                        let visibleParts =
                            [ "get", accessors.Getter; "set", accessors.Setter ]
                            |> List.filter (fun (_, h) -> not h.IsNil)
                            |> List.filter (fun (_, h) ->
                                let am = r.GetMethodDefinition h
                                visibleMethodAccess.Contains(am.Attributes &&& MethodAttributes.MemberAccessMask))
                            |> List.map fst

                        if not visibleParts.IsEmpty then
                            let attrs = pd.GetCustomAttributes()
                            let map = mappingInts r attrs

                            let construct2 =
                                match map with
                                | Some(flags :: _) -> flags &&& SourceConstructMask
                                | _ -> -1

                            let isRecordField = kind = "record" && construct2 = SourceConstructField

                            let isUnionCase = construct2 = SourceConstructUnionCase

                            let skip =
                                not (isRecordField || isUnionCase)
                                && hasAttribute r attrs "CompilerGeneratedAttribute"

                            if not skip then
                                let ty = pd.DecodeSignature(sigProvider, null).ReturnType

                                let ordinal =
                                    match map with
                                    | Some ints when ints.Length >= 2 -> List.last ints
                                    | _ -> 0

                                if isRecordField then
                                    tokens.Add(sprintf "record-field %s.%s #%d : %s" full name ordinal ty)
                                elif isUnionCase then
                                    tokens.Add(sprintf "union-case %s.%s #%d()" full name ordinal)
                                else
                                    tokens.Add(
                                        sprintf
                                            "property %s.%s : %s { %s }"
                                            full
                                            name
                                            ty
                                            (String.concat "; " visibleParts)
                                    )
                    with e ->
                        eprintfn "surface: property skipped in %s — %s" full e.Message

                // -- fields --
                for fh in td.GetFields() do
                    try
                        let fd = r.GetFieldDefinition fh
                        let access = fd.Attributes &&& FieldAttributes.FieldAccessMask

                        let visible =
                            access = FieldAttributes.Public
                            || access = FieldAttributes.Family
                            || access = FieldAttributes.FamORAssem

                        let name = r.GetString fd.Name

                        if
                            visible
                            && not (name.Contains '@')
                            && not (hasAttribute r (fd.GetCustomAttributes()) "CompilerGeneratedAttribute")
                        then
                            let ty = fd.DecodeSignature(sigProvider, null)

                            let literal =
                                if fd.Attributes.HasFlag FieldAttributes.Literal then
                                    " (literal)"
                                else
                                    ""

                            tokens.Add(sprintf "field %s.%s : %s%s" full name ty literal)
                    with e ->
                        eprintfn "surface: field skipped in %s — %s" full e.Message

                if isInterface then
                    // Recorded so the differ can classify a member ADDED to this type as
                    // breaking without re-reading the assembly: every implementer of an
                    // interface stops compiling when the interface grows a member.
                    tokens.Add(sprintf "interface-marker %s" full)

        tokens |> List.ofSeq |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

// ---- the baseline file ----------------------------------------------------

/// The header every baseline carries. Static apart from the package id — a timestamp or a
/// version here would make every regeneration a diff.
let internal baselineHeader (packageId: string) : string list =
    [ sprintf "# %s — public contract surface (Phase 183)." packageId
      "# GENERATED from the built assembly's IL metadata. Do not hand-edit: regenerate with"
      "#   CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Compute.Tests"
      "# and stage the baselines you meant to move BY NAME — the switch rewrites them all."
      "# One ordinal-sorted line per externally-visible type, member, record field and union case."
      "# See STABILITY.md \"Public-surface baselines\" for what each move class means." ]

/// The exact bytes a baseline file holds, rendered without writing anything — the same
/// render/emit separation `LawVectorExport` keeps, and for the same reason: a byte-for-byte
/// guard has to be able to ask "is the committed file current?" on a clean tree, and one that
/// had to write in order to compare could not. LF, explicitly: `.gitattributes` pins LF in the
/// index and `WorkingCopyEolTests` fails a working copy that has drifted.
let internal renderBaseline (packageId: string) (tokens: string list) : string =
    (baselineHeader packageId @ tokens |> String.concat "\n") + "\n"

/// The tokens of a baseline text: every non-blank line that is not a comment.
let internal baselineTokens (text: string) : string list =
    text.Replace("\r\n", "\n").Split('\n')
    |> Array.toList
    |> List.map _.TrimEnd()
    |> List.filter (fun l -> l <> "" && not (l.StartsWith("#", StringComparison.Ordinal)))

// ---- the classifier -------------------------------------------------------

/// How a single moved token relates to the baseline.
///
/// The declaration order is NOT what `headline` reads — see `headline` for the order that
/// decides a surface's one-word class, and why it is a decision rather than a listing.
type internal MoveClass =
    | Removal
    | Retype
    | RecordWidening
    | UnionWidening
    | InterfaceWidening
    | Additive

let internal className (c: MoveClass) : string =
    match c with
    | Removal -> "removal"
    | Retype -> "retype"
    | RecordWidening -> "record-widening"
    | UnionWidening -> "union-widening"
    | InterfaceWidening -> "interface-widening"
    | Additive -> "additive"

/// `true` for every class that stops a pinned consumer compiling (or, for a union case against
/// a stale same-version pack, loading). Only `Additive` is safe.
let internal isBreaking (c: MoveClass) : bool = c <> Additive

type internal Move =
    {
        Class: MoveClass
        /// The baseline's token, where there was one.
        Before: string option
        /// The current surface's token, where there is one.
        After: string option
    }

/// The identity of a token — everything up to its signature, type or ordinal. Two tokens
/// sharing an identity are the same MEMBER rendered differently, which is what makes `retype`
/// distinguishable from a removal beside an unrelated addition.
let internal identity (token: string) : string =
    let space = token.IndexOf ' '

    if space < 0 then
        token
    else
        let kind = token.Substring(0, space)
        let rest = token.Substring(space + 1)

        let cuts =
            [ rest.IndexOf '('; rest.IndexOf " #"; rest.IndexOf " : "; rest.IndexOf " (" ]
            |> List.filter (fun i -> i >= 0)

        let body =
            match cuts with
            | [] -> rest
            | _ -> rest.Substring(0, List.min cuts)

        kind + " " + body.TrimEnd()

/// The owning type of a member token — the qualified name with its last segment removed.
/// `None` for a token that names no member (`type`, `interface-marker`).
///
/// Derived from `identity` rather than from the raw token, because a rendered signature
/// carries both spaces and dots (`method X.Go(System.Int32, System.String) : …`), so splitting
/// the token on whitespace and taking the last dot lands INSIDE a parameter type. That is not
/// a hypothetical: it read `X.Go(System` as the owner and silently classified a member added
/// to a published interface as additive.
let internal owner (token: string) : string option =
    let id = identity token
    let space = id.IndexOf ' '

    if space < 0 then
        None
    else
        let body = id.Substring(space + 1)
        let dot = body.LastIndexOf '.'

        if dot <= 0 then
            None
        else
            Some(body.Substring(0, dot).TrimEnd '.')

/// Classify the drift from `before` to `after`, as a list of moves.
///
/// Three steps, and the middle one is what a set difference alone cannot do: removed and added
/// tokens sharing an identity are PAIRED into one `Retype`, but only when exactly one of each
/// carries that identity. An overload set where two members changed is left as separate
/// removals and additions rather than paired arbitrarily — a wrong pairing reads as a smaller
/// change than actually happened, which is the one direction this must not err in.
let internal classify (before: string list) (after: string list) : Move list =
    let beforeSet = Set.ofList before
    let afterSet = Set.ofList after

    let removed =
        before
        |> List.filter (fun t -> not (afterSet.Contains t))
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    let added =
        after
        |> List.filter (fun t -> not (beforeSet.Contains t))
        |> List.sortWith (fun a b -> String.CompareOrdinal(a, b))

    let countBy f xs =
        xs
        |> List.countBy f
        |> Map.ofList
        |> fun m k -> Map.tryFind k m |> Option.defaultValue 0

    let removedCount = countBy identity removed
    let addedCount = countBy identity added

    let pairable (t: string) =
        removedCount (identity t) = 1 && addedCount (identity t) = 1

    let retypes =
        removed
        |> List.filter pairable
        |> List.map (fun r ->
            let a = added |> List.find (fun t -> identity t = identity r)

            { Class = Retype
              Before = Some r
              After = Some a })

    // Interfaces the BASELINE published. An interface that is itself new carries its whole
    // member set as additions, and those are additive — nobody implements it yet.
    let baselineInterfaces =
        before
        |> List.choose (fun t ->
            if t.StartsWith("interface-marker ", StringComparison.Ordinal) then
                Some(t.Substring("interface-marker ".Length))
            else
                None)
        |> Set.ofList

    let publishedRecord (o: string) =
        beforeSet.Contains(sprintf "type %s (record)" o)

    let publishedUnion (o: string) =
        beforeSet.Contains(sprintf "type %s (union)" o)

    let classifyAdded (t: string) : MoveClass =
        // A field or case on a type the baseline never published is a NEW type: nobody could
        // have constructed or matched it, so it is additive. Checking the OWNER is what keeps
        // a whole new record from being reported as a pile of breaking widenings.
        match owner t with
        | Some o when t.StartsWith("record-field ", StringComparison.Ordinal) && publishedRecord o -> RecordWidening
        | Some o when t.StartsWith("union-case ", StringComparison.Ordinal) && publishedUnion o -> UnionWidening
        | Some o when
            (t.StartsWith("method ", StringComparison.Ordinal)
             || t.StartsWith("property ", StringComparison.Ordinal))
            && baselineInterfaces.Contains o
            ->
            InterfaceWidening
        | _ -> Additive

    let removals =
        removed
        |> List.filter (pairable >> not)
        |> List.map (fun t ->
            { Class = Removal
              Before = Some t
              After = None })

    let additions =
        added
        |> List.filter (pairable >> not)
        |> List.map (fun t ->
            { Class = classifyAdded t
              Before = None
              After = Some t })

    removals @ retypes @ additions

/// The one-word class of a whole surface move, or `None` when the surface did not move. This
/// is the "ride or advance" line's verdict.
///
/// The order below ranks by how much the word tells a reader about what the author DID, not by
/// how badly each shape breaks — the first five all break a pinned consumer, so a severity
/// ranking among them would be a distinction without a difference. `Retype` sits BELOW the
/// three widenings deliberately, because it is usually their shadow rather than an independent
/// move: adding a required record field emits the field AND widens the compiler-generated
/// primary constructor, so the surface shows one `record-widening` beside one `retype`. Ranking
/// retype first named that move after its side effect. Every move is printed regardless — the
/// headline summarises a list the reader is also shown, never one it replaces.
let internal headline (moves: Move list) : MoveClass option =
    let order =
        [ Removal; RecordWidening; UnionWidening; InterfaceWidening; Retype; Additive ]

    order |> List.tryFind (fun c -> moves |> List.exists (fun m -> m.Class = c))

// ---- union field names (Phase 237) ------------------------------------------
//
// A `union-case` token's parameter list reads `name: Type, …` since Phase 237. A rename of a
// field changes the token and leaves its identity (everything before the `(`) alone, so the
// pairing step above already classes it `retype`; what these add is the REPORT — naming the
// case and both field names rather than leaving a reader to spot the difference between two
// long tokens — and the legacy-format reading the since-tag report needs.

/// The top-level comma-separated items of a parameter list — a generic instantiation carries
/// `, ` inside its angle brackets, so a plain split would cut `Map<K, V>` in two.
let private splitParameters (ps: string) : string list =
    let items = ResizeArray<string>()
    let current = StringBuilder()
    let mutable depth = 0

    for c in ps do
        match c with
        | '<' ->
            depth <- depth + 1
            current.Append c |> ignore
        | '>' ->
            depth <- depth - 1
            current.Append c |> ignore
        | ',' when depth = 0 ->
            items.Add(current.ToString().Trim())
            current.Clear() |> ignore
        | _ -> current.Append c |> ignore

    let last = current.ToString().Trim()

    if last <> "" || items.Count > 0 then
        items.Add last

    List.ofSeq items

/// A `union-case` token's fields as `(name, type)` — `None` for the name when the token
/// carries none (the pre-Phase-237 format). Empty for a nullary case or any other token.
let internal unionCaseFields (token: string) : (string option * string) list =
    let o = token.IndexOf '('

    if
        not (token.StartsWith("union-case ", StringComparison.Ordinal))
        || o < 0
        || not (token.EndsWith ")")
    then
        []
    else
        token.Substring(o + 1, token.Length - o - 2)
        |> splitParameters
        |> List.map (fun p ->
            // A rendered type never carries `: `; a field name never carries `<`.
            let colon = p.IndexOf ": "
            let angle = p.IndexOf '<'

            if colon > 0 && (angle < 0 || colon < angle) then
                Some(p.Substring(0, colon)), p.Substring(colon + 2)
            else
                None, p)

/// The field renames a `retype` move carries: `(case, before, after)` for every position whose
/// name changed while its type did not. Empty for any other move — including a retype that
/// changed a field's TYPE, which `describe` reports as the plain before/after pair.
let internal fieldRenames (m: Move) : (string * string * string) list =
    match m.Class, m.Before, m.After with
    | Retype, Some b, Some a when b.StartsWith("union-case ", StringComparison.Ordinal) ->
        let bf = unionCaseFields b
        let af = unionCaseFields a

        if bf.Length <> af.Length then
            []
        else
            let case = (identity b).Substring("union-case ".Length)

            List.zip bf af
            |> List.choose (fun ((bn, bt), (an, at)) ->
                match bn, an with
                | Some x, Some y when x <> y && bt = at -> Some(case, x, y)
                | _ -> None)
    | _ -> []

/// A token with its union field names removed — the pre-Phase-237 rendering of the same
/// surface. Used ONLY to compare against a baseline that predates the names (the since-tag
/// report); the live gate never normalises, because a nameless comparison is exactly the
/// blindness Phase 237 removes.
let internal stripFieldNames (token: string) : string =
    match unionCaseFields token with
    | [] -> token
    | fields ->
        let o = token.IndexOf '('
        token.Substring(0, o + 1) + String.Join(", ", fields |> List.map snd) + ")"

/// `true` when a baseline predates field names: it carries a union case with fields and not
/// one of them names a field. A baseline with no carrying case at all is not legacy — there is
/// nothing a name would have been added to.
let internal predatesFieldNames (tokens: string list) : bool =
    let carrying =
        tokens |> List.map unionCaseFields |> List.filter (List.isEmpty >> not)

    not carrying.IsEmpty
    && carrying |> List.forall (List.forall (fst >> Option.isNone))

/// A move rendered for a human, one line.
let internal describe (m: Move) : string =
    match m.Before, m.After with
    | Some b, Some a when not (fieldRenames m).IsEmpty ->
        let renames =
            fieldRenames m
            |> List.map (fun (case, x, y) -> sprintf "field rename on %s: %s -> %s" case x y)
            |> String.concat "; "

        sprintf "%-18s %s  (%s  ->  %s)" (className m.Class) renames b a
    | Some b, Some a -> sprintf "%-18s %s  ->  %s" (className m.Class) b a
    | Some b, None -> sprintf "%-18s - %s" (className m.Class) b
    | None, Some a -> sprintf "%-18s + %s" (className m.Class) a
    | None, None -> className m.Class

// ---- locating what to render ----------------------------------------------

let internal apiDir () : string = Snapshots.repoFile "api"

let internal baselinePath (packageId: string) : string =
    Path.Combine(apiDir (), packageId + ".txt")

/// The configuration this test binary was built into — `Debug` / `Release` — read off its own
/// output path, so the surface is rendered from the same build the suite is running under
/// rather than from whatever else happens to be on disk.
let internal ownConfiguration () : string option =
    let dir = DirectoryInfo AppContext.BaseDirectory

    match dir.Parent with
    | null -> None
    | tfmParent ->
        match tfmParent.Parent with
        | null -> None
        | configDir when configDir.Name = "bin" -> Some tfmParent.Name
        | configDir -> Some configDir.Name

/// The built assembly for a packable project, preferring this binary's own configuration.
/// `Error` names what was looked for — a missing assembly means the solution was not built,
/// which is a different failure from a surface that moved and must not read as one.
let internal assemblyFor (root: string) (projectFile: string) (packageId: string) : Result<string, string> =
    let projectDir = Path.Combine(root, Path.GetDirectoryName(projectFile: string))
    let binDir = Path.Combine(projectDir, "bin")

    if not (Directory.Exists binDir) then
        Error(sprintf "%s: no bin/ under %s — build the solution first" packageId projectDir)
    else
        let candidates =
            Directory.GetFiles(binDir, packageId + ".dll", SearchOption.AllDirectories)
            |> Array.toList

        let preferred =
            match ownConfiguration () with
            | Some config ->
                candidates
                |> List.filter (fun p ->
                    p.Replace('\\', '/').Contains("/bin/" + config + "/", StringComparison.OrdinalIgnoreCase))
            | None -> []

        match preferred @ candidates with
        | best :: _ -> Ok best
        | [] -> Error(sprintf "%s: no %s.dll under %s — build the solution first" packageId packageId binDir)

let private repoRoot () : string = Snapshots.repoFile ""

// ---- git, for the since-the-newest-tag report -----------------------------

let private git (root: string) (arguments: string) : Result<string, string> =
    try
        let psi = ChildProcess.redirected "git" arguments
        psi.WorkingDirectory <- root
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "`git %s` exited %d: %s" arguments p.ExitCode (err.Trim()))
        else
            Ok out
    with e ->
        Error("`git` could not be run: " + e.Message)

/// Semantic ordering over `vX.Y.Z` tags. A tag this does not parse is dropped rather than
/// sorted lexically — `v0.9.0` above `v0.26.0` would name the wrong baseline to compare
/// against, and a wrong comparison is worse than none.
let internal newestVersionTag (tagLines: string list) : string option =
    tagLines
    |> List.map _.Trim()
    |> List.choose (fun t ->
        if not (t.StartsWith("v", StringComparison.Ordinal)) then
            None
        else
            match t.Substring(1).Split('.') with
            | [| a; b; c |] ->
                match Int32.TryParse a, Int32.TryParse b, Int32.TryParse c with
                | (true, x), (true, y), (true, z) -> Some((x, y, z), t)
                | _ -> None
            | _ -> None)
    |> function
        | [] -> None
        | xs -> xs |> List.maxBy fst |> snd |> Some

// ---- the suite ------------------------------------------------------------

let private approving () =
    match Environment.GetEnvironmentVariable "CORE_APPROVE_API" with
    | null -> false
    | v -> v.Trim() <> "" && v.Trim() <> "0"

/// The packable roster, from Phase 199's derivation. ONE spelling of "what ships" in this
/// repository, deliberately: a second would drift from the first exactly the way the documents
/// that derivation gates had drifted.
let private roster () =
    PackageRosterTests.packableProjects (repoRoot ())

[<Tests>]
let tests =
    testList
        "Public surface"
        [

          test "every packable package has a committed baseline, and no baseline is orphaned" {
              let root = repoRoot ()
              let packable = roster () |> List.map _.PackageId
              Expect.isNonEmpty packable "at least one packable project was found under src/"

              let dir = apiDir ()

              let committed =
                  if Directory.Exists dir then
                      Directory.GetFiles(dir, "*.txt")
                      |> Array.map Path.GetFileNameWithoutExtension
                      |> Array.toList
                      |> List.sort
                  else
                      []

              let missing = packable |> List.filter (fun p -> not (List.contains p committed))
              let orphaned = committed |> List.filter (fun c -> not (List.contains c packable))

              match missing, orphaned with
              | [], [] -> ()
              | _ ->
                  let part label items =
                      if List.isEmpty items then
                          ""
                      else
                          sprintf "\n       %s: %s" label (String.concat ", " items)

                  failtestf
                      "the committed baselines under api/ are not the packable set.%s%s\n       Remedy: CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Compute.Tests writes a baseline for every packable package; delete an orphan by hand. (root: %s)"
                      (part "packable but unbaselined" missing)
                      (part "baselined but not packable" orphaned)
                      root
          }

          test "every baseline is the surface its assembly renders today" {
              let root = repoRoot ()
              let projects = roster ()
              Expect.isNonEmpty projects "at least one packable project was found under src/"

              let dir = apiDir ()

              if approving () then
                  Directory.CreateDirectory dir |> ignore

              let unbuilt = ResizeArray<string>()
              let drifted = ResizeArray<string>()
              let rewritten = ResizeArray<string>()

              for p in projects do
                  match assemblyFor root p.ProjectFile p.PackageId with
                  | Error why -> unbuilt.Add why
                  | Ok dll ->
                      let rendered = renderAssembly dll
                      let text = renderBaseline p.PackageId rendered
                      let path = baselinePath p.PackageId

                      if approving () then
                          let existing = if File.Exists path then File.ReadAllText path else ""

                          if existing <> text then
                              File.WriteAllText(path, text, UTF8Encoding false)
                              rewritten.Add p.PackageId
                      elif not (File.Exists path) then
                          // Reported by the completeness leg above; nothing to compare here.
                          ()
                      else
                          let baseline = baselineTokens (File.ReadAllText path)

                          match classify baseline rendered with
                          | [] -> ()
                          | moves ->
                              let cls = headline moves |> Option.map className |> Option.defaultValue "unchanged"

                              let shown =
                                  moves
                                  |> List.truncate 12
                                  |> List.map (fun m -> "         " + describe m)
                                  |> String.concat "\n"

                              let more =
                                  if moves.Length > 12 then
                                      sprintf "\n         ... and %d more" (moves.Length - 12)
                                  else
                                      ""

                              drifted.Add(sprintf "%s — %s (%d move(s))\n%s%s" p.PackageId cls moves.Length shown more)

              if approving () then
                  if rewritten.Count = 0 then
                      printfn "CORE_APPROVE_API: every baseline was already current."
                  else
                      printfn
                          "CORE_APPROVE_API: rewrote %d baseline(s): %s"
                          rewritten.Count
                          (String.concat ", " rewritten)

                      printfn
                          "CORE_APPROVE_API: this rewrites EVERY drifted baseline, not only the one you were looking at — stage them by name."

              Expect.isEmpty
                  (List.ofSeq unbuilt)
                  (sprintf
                      "every packable project's assembly was found on disk; a missing one is an unbuilt solution, not a surface move:\n       %s"
                      (String.concat "\n       " unbuilt))

              if drifted.Count > 0 && not (approving ()) then
                  failtestf
                      "%d package(s) moved their public surface without moving their baseline:\n       %s\n       Remedy: the move is PERMITTED — what is refused is an UNCLASSIFIED one. Regenerate with\n       `CORE_APPROVE_API=1 dotnet run --project tests/Fuaran.Core.Compute.Tests`, stage the baselines you meant\n       to move BY NAME, and let the class above decide whether the change RIDES the standing draft slot\n       or ADVANCES <Version> (STABILITY.md, \"Public-surface baselines\")."
                      drifted.Count
                      (String.concat "\n       " (List.ofSeq drifted))
          }

          test "the class of every baseline moved since the newest tag" {
              // The "ride or advance" line, which STABILITY entries wrote by hand until now.
              // A REPORT and not a gate: the class is what a reviewer applies the
              // record-widening dispensation to, so a breaking class printed here is
              // information, never a refusal. What it does assert is that it MEASURED
              // something — a newest tag was resolved and every baseline was read — because a
              // report that silently compared nothing is the one outcome worse than no report.
              let root = repoRoot ()

              match git root "tag --list" with
              | Error why ->
                  printfn "PublicSurface since-tag report SKIPPED: %s" why
                  skiptestf "since-tag class report skipped — %s" why
              | Ok out ->
                  let tags =
                      out.Split('\n')
                      |> Array.toList
                      |> List.map _.Trim()
                      |> List.filter (fun l -> l <> "")

                  match newestVersionTag tags with
                  | None ->
                      printfn "PublicSurface since-tag report SKIPPED: no `vX.Y.Z` tag in this clone"
                      skiptest "since-tag class report skipped — this clone holds no `vX.Y.Z` tag"
                  | Some tag ->
                      let packable = roster () |> List.map _.PackageId
                      Expect.isNonEmpty packable "at least one packable project was found under src/"

                      printfn ""
                      printfn "==== public surface: class of every baseline moved since %s" tag

                      let mutable read = 0
                      let mutable moved = 0

                      for id in packable do
                          let path = baselinePath id

                          if File.Exists path then
                              read <- read + 1
                              let current = baselineTokens (File.ReadAllText path)

                              match git root (sprintf "show %s:api/%s.txt" tag id) with
                              | Error _ ->
                                  printfn "  %-28s first snapshot — %s carries no baseline for this package" id tag
                              | Ok text ->
                                  // A tag cut before Phase 237 carries nameless union cases. Read
                                  // against it, today's named baseline would report every carrying
                                  // case as a `retype` that no consumer ever saw. So the comparison
                                  // drops the names — and says so, because a field rename since
                                  // that tag is invisible to it, exactly as it was to the gate then.
                                  // This expires by itself: the first tag cut after 237 carries
                                  // named baselines, and nothing is stripped against it.
                                  let tagged = baselineTokens text

                                  let current =
                                      if predatesFieldNames tagged then
                                          printfn
                                              "  %-28s (%s's baseline predates union field names — compared without them; a field rename since %s is not visible here)"
                                              id
                                              tag
                                              tag

                                          current |> List.map stripFieldNames
                                      else
                                          current

                                  match classify tagged current with
                                  | [] -> ()
                                  | moves ->
                                      moved <- moved + 1

                                      let cls =
                                          headline moves |> Option.map className |> Option.defaultValue "unchanged"

                                      printfn
                                          "  %-28s %-18s %d move(s)%s"
                                          id
                                          cls
                                          moves.Length
                                          (if isBreaking (headline moves |> Option.defaultValue Additive) then
                                               "   ADVANCE <Version> — this move is breaking"
                                           else
                                               "   may RIDE a draft slot")

                                      for m in moves |> List.truncate 8 do
                                          printfn "      %s" (describe m)

                                      if moves.Length > 8 then
                                          printfn "      ... and %d more" (moves.Length - 8)

                      if moved = 0 then
                          printfn "  (no baseline has moved since %s)" tag

                      printfn "==== %d baseline(s) read, %d moved" read moved
                      printfn ""

                      Expect.isGreaterThan
                          read
                          0
                          "at least one committed baseline was read — a report that compared nothing must not read as a report that found nothing"
          }

          // ---- the renderer, pinned against types declared in this assembly --------
          //
          // Every leg above compares a rendered surface with a file THIS renderer wrote, a
          // pairing that would agree perfectly with itself if the renderer emitted nothing.
          // These read the test assembly's own metadata, where the expected text is known
          // from the declarations at the head of this file.

          test "the renderer emits the F# construct tokens the classifier turns on" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)
              Expect.isNonEmpty own "the test assembly's own surface rendered"

              let endsWith (suffix: string) (t: string) =
                  t.EndsWith(suffix, StringComparison.Ordinal)

              let recordType =
                  own
                  |> List.tryFind (fun t ->
                      t.StartsWith("type ", StringComparison.Ordinal)
                      && endsWith "+ProbeRecord (record)" t)

              Expect.isSome recordType "a record declared in this module renders as `type <full> (record)`"

              let fields =
                  own
                  |> List.filter (fun t ->
                      t.StartsWith("record-field ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord.")
                  |> List.map (fun t -> t.Substring(t.IndexOf "+ProbeRecord." + "+ProbeRecord.".Length))
                  |> List.sort

              Expect.equal
                  fields
                  [ "Alpha #0 : System.Int32"; "Beta #1 : System.String" ]
                  "both record fields render with their declaration ORDER — the ordinal is what makes a reorder a `retype` rather than nothing"

              let unionType = own |> List.tryFind (fun t -> endsWith "+ProbeUnion (union)" t)

              Expect.isSome unionType "a union renders as `type <full> (union)`"

              let cases =
                  own
                  |> List.filter (fun t ->
                      t.StartsWith("union-case ", StringComparison.Ordinal)
                      && t.Contains "+ProbeUnion.")
                  |> List.map (fun t -> t.Substring(t.IndexOf "+ProbeUnion." + "+ProbeUnion.".Length))
                  |> List.sort

              Expect.equal
                  cases
                  [ "NewProbeCarrying #1(Item1: System.Int32, Item2: System.String)"
                    "ProbeEmpty #0()" ]
                  "a nullary case renders from its property and a carrying case from its factory — the factory is `CompilerGenerated`, so keeping it is the deliberate carve-out the union-widening class rests on; an unnamed field renders under the name the compiler gives it (Phase 237)"

              let caseOf (typeSuffix: string) =
                  own
                  |> List.filter (fun t ->
                      t.StartsWith("union-case ", StringComparison.Ordinal)
                      && t.Contains(typeSuffix + "."))
                  |> List.map (fun t -> t.Substring(t.IndexOf(typeSuffix + ".") + typeSuffix.Length + 1))
                  |> List.sort

              Expect.equal
                  (caseOf "+ProbeSingle")
                  [ "NewProbeSingle #0(value: System.Int32)" ]
                  "a single-case union's field is named — its property sits on the union type and carries the two-argument mapping (Phase 237)"

              Expect.equal
                  (caseOf "+ProbeStructUnion")
                  [ "NewProbeStructA #0(a: System.Int32)"
                    "NewProbeStructB #1(b: System.String, c: System.Int32)" ]
                  "a struct union's fields are named per case, in declaration order (Phase 237)"

              Expect.equal
                  (caseOf "+ProbeFieldsBefore+Shape")
                  [ "Empty #0()"; "NewHeld #1(parent: System.Int32, kindTag: System.String)" ]
                  "a multi-case union's fields are named from the case class's properties, in declaration order (Phase 237)"

              Expect.isSome
                  (own
                   |> List.tryFind (fun t ->
                       endsWith "+IProbeSeam" t
                       && t.StartsWith("interface-marker ", StringComparison.Ordinal)))
                  "an interface carries its marker, which is what lets an ADDED member on it be classified breaking"

              Expect.isSome
                  (own
                   |> List.tryFind (fun t ->
                       t.StartsWith("method ", StringComparison.Ordinal)
                       && t.Contains "+IProbeSeam.Probe"))
                  "the interface's own member is rendered"
          }

          test "the renderer excludes what a consumer cannot reach" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              // Measured over the token's IDENTITY — its declaring type and member name — and
              // not over the whole token: a rendered signature legitimately carries `<` for a
              // generic instantiation (`FSharpFunc`2<!0, System.String>`), so the whole-token
              // reading would call every generic member a lowered artefact.
              Expect.isEmpty
                  (own
                   |> List.filter (fun t ->
                       let id = identity t
                       id.Contains '@' || id.Contains '<'))
                  "no lowered artefact (closure, state machine, startup class) reaches the surface"

              Expect.isEmpty
                  (own |> List.filter (fun t -> t.Contains ".PackageRosterTests.isPackable"))
                  "an `internal` function is not surface — it cannot break a consumer in another assembly"

              Expect.isEmpty
                  (own
                   |> List.filter (fun t ->
                       t.StartsWith("method ", StringComparison.Ordinal)
                       && (t.Contains ".get_" || t.Contains ".set_")))
                  "property accessors are rendered with their property, never twice"
          }

          // ---- the go-red controls ------------------------------------------
          //
          // Each perturbation below is applied to a REAL rendered surface — the test
          // assembly's own — rather than to a hand-written fixture, so the classifier is shown
          // to fire on the token shapes the renderer actually produces. A synthetic table
          // would prove the classifier consistent with itself and nothing more.

          test "a removed token is classified `removal`" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let victim =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("record-field ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord.Alpha")

              let after = before |> List.filter (fun t -> t <> victim)

              let moves = classify before after
              Expect.equal (headline moves) (Some Removal) "dropping a published record field is a removal"
              Expect.equal moves.Length 1 "exactly one move is reported"
              Expect.equal (moves.Head.Before) (Some victim) "the move names the token that went"
              Expect.isTrue (isBreaking Removal) "a removal is breaking"
          }

          test "a retyped member is classified `retype`, as ONE move rather than two" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let victim =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("record-field ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord.Beta")

              let retyped = victim.Replace("System.String", "System.Int32")

              let after = before |> List.map (fun t -> if t = victim then retyped else t)

              let moves = classify before after
              Expect.equal (headline moves) (Some Retype) "the same member rendered differently is a retype"

              Expect.equal
                  moves.Length
                  1
                  "a retype is ONE move — reporting it as a removal beside an addition would double-count it and mis-name the addition's class"

              Expect.equal moves.Head.After (Some retyped) "the move carries both sides"
          }

          test "a reordered record field is classified `retype` too" {
              // The shape a set difference sees as nothing at all if the ordinal is dropped
              // from the token, which is why the renderer emits `#seq`.
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let alpha =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("record-field ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord.Alpha")

              let after =
                  before |> List.map (fun t -> if t = alpha then alpha.Replace("#0", "#1") else t)

              Expect.equal
                  (headline (classify before after))
                  (Some Retype)
                  "a field that moved position is reported — positional construction would bind the wrong slot"
          }

          // ---- Phase 237: a union field rename is a surface move ----------------
          //
          // Phase 228 renamed `DiffError.TargetNotAContainer`'s field `parent` -> `target`, and
          // this gate printed nothing: a case rendered by its field TYPES only. These legs pin
          // the move on a compiled fixture pair, on its control, and on 228's rename itself.

          test
              "a union field rename is a breaking `retype` naming the case and both names; an identical pair is unchanged (Phase 237)" {
              let own = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              // One module's surface, re-homed under a common name so the three are comparable
              // as one package seen at two points in time.
              let surfaceOf (moduleName: string) =
                  own
                  |> List.filter (fun t -> t.Contains("+" + moduleName + "+"))
                  |> List.map (fun t -> t.Replace("+" + moduleName + "+", "+ProbeFields+"))

              let before = surfaceOf "ProbeFieldsBefore"
              let after = surfaceOf "ProbeFieldsAfter"
              let same = surfaceOf "ProbeFieldsSame"

              Expect.isNonEmpty before "the fixture's surface rendered"

              Expect.equal
                  (List.length before)
                  (List.length after)
                  "the pair differs by no token count — only by one field's name"

              Expect.isEmpty (classify before same) "a pair differing by nothing is classed unchanged"
              Expect.equal (headline (classify before same)) None "and has no headline"

              let moves = classify before after

              Expect.equal
                  (moves |> List.map _.Class)
                  [ Retype ]
                  "a pair differing only by a union field name is ONE move, a `retype` — not a removal beside an addition"

              Expect.isTrue
                  (headline moves |> Option.map isBreaking |> Option.defaultValue false)
                  "and it is BREAKING: a consumer constructing or matching the case by field name stops compiling"

              let case = "Fuaran.Core.Tests.PublicSurfaceTests+ProbeFields+Shape.NewHeld"

              Expect.equal
                  (moves |> List.collect fieldRenames)
                  [ case, "parent", "target" ]
                  "the move names the case and both field names"

              let line = describe (List.head moves)

              Expect.stringContains
                  line
                  (sprintf "field rename on %s: parent -> target" case)
                  "the report a reviewer reads names the case and both names"
          }
          test "every carrying union case in every packable package renders its field names (Phase 237)" {
              // The renderer falls back to types alone for a case whose fields it could not
              // name; this is what turns that fallback into a failure rather than a quiet
              // return of the blindness.
              let root = repoRoot ()

              let unnamed =
                  [ for p in roster () do
                        match assemblyFor root p.ProjectFile p.PackageId with
                        | Error _ -> () // reported by the baseline leg as an unbuilt solution
                        | Ok dll ->
                            for t in renderAssembly dll do
                                if unionCaseFields t |> List.exists (fst >> Option.isNone) then
                                    yield sprintf "%s: %s" p.PackageId t ]

              Expect.isEmpty unnamed "every union case with fields renders `name: Type` for each"
          }

          test "the since-tag report reads a pre-237 baseline without names, and only it (Phase 237)" {
              let named = [ "type A.U (union)"; "union-case A.U.NewC #0(target: System.Int32)" ]

              let legacy = named |> List.map stripFieldNames

              Expect.equal legacy [ "type A.U (union)"; "union-case A.U.NewC #0(System.Int32)" ] "names stripped"
              Expect.isTrue (predatesFieldNames legacy) "a nameless baseline with a carrying case predates the names"
              Expect.isFalse (predatesFieldNames named) "a named one does not"

              Expect.isFalse
                  (predatesFieldNames [ "type A.U (union)"; "union-case A.U.C #0()" ])
                  "a baseline with no carrying case has nothing a name was added to"

              Expect.isEmpty
                  (classify legacy (named |> List.map stripFieldNames))
                  "a legacy baseline read against a stripped current surface is unchanged"

              Expect.equal
                  (classify legacy named |> List.map _.Class)
                  [ Retype ]
                  "while the LIVE gate, which never strips, sees a nameless baseline as moved — so a stale baseline cannot hide a rename"
          }

          test "a field added to a PUBLISHED record is `record-widening`, and to a new one is `additive`" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let recordType =
                  before
                  |> List.find (fun t -> t.EndsWith("+ProbeRecord (record)", StringComparison.Ordinal))

              let ownerName =
                  recordType.Substring("type ".Length, recordType.Length - "type ".Length - " (record)".Length)

              let widened =
                  (sprintf "record-field %s.Gamma #2 : System.Int32" ownerName) :: before

              Expect.equal
                  (headline (classify before widened))
                  (Some RecordWidening)
                  "a field on a record the baseline published breaks every full-literal construction (FS0764)"

              // The control: the SAME token shape on a type the baseline never had.
              let fresh =
                  [ "type Fuaran.Core.Probe.Brand (record)"
                    "record-field Fuaran.Core.Probe.Brand.Gamma #0 : System.Int32" ]
                  @ before

              Expect.equal
                  (headline (classify before fresh))
                  (Some Additive)
                  "a field on a type the baseline never published is additive — nobody could have constructed it"

              // The shape a REAL widening takes, measured rather than assumed: adding a
              // required field also widens the compiler-generated primary constructor, so the
              // surface carries a `retype` beside the `record-widening`. The headline must
              // name the CAUSE, not its side effect.
              let ctor =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("ctor ", StringComparison.Ordinal)
                      && t.Contains "+ProbeRecord..ctor")

              let widenedWithCtor =
                  widened
                  |> List.map (fun t -> if t = ctor then ctor.Replace(")", ", System.Int32)") else t)

              let moves = classify before widenedWithCtor

              Expect.isTrue
                  (moves |> List.exists (fun m -> m.Class = Retype))
                  "the widened constructor is present as a retype"

              Expect.equal
                  (headline moves)
                  (Some RecordWidening)
                  "the headline names the widening, not the constructor retype it caused"
          }

          test "a case added to a PUBLISHED union is `union-widening`, and to a new one is `additive`" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let unionType =
                  before
                  |> List.find (fun t -> t.EndsWith("+ProbeUnion (union)", StringComparison.Ordinal))

              let ownerName =
                  unionType.Substring("type ".Length, unionType.Length - "type ".Length - " (union)".Length)

              let widened =
                  (sprintf "union-case %s.NewProbeThird #2(System.Int32)" ownerName) :: before

              Expect.equal
                  (headline (classify before widened))
                  (Some UnionWidening)
                  "a case on a union the baseline published makes every exhaustive match incomplete — and REMOVES NOTHING, which is why a removal-only differ misses it"

              let fresh =
                  [ "type Fuaran.Core.Probe.Choice (union)"
                    "union-case Fuaran.Core.Probe.Choice.NewOne #0()" ]
                  @ before

              Expect.equal
                  (headline (classify before fresh))
                  (Some Additive)
                  "a case on a union the baseline never published is additive"
          }

          test "a member added to a PUBLISHED interface is `interface-widening`, and to a new one is `additive`" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              let marker =
                  before
                  |> List.find (fun t ->
                      t.StartsWith("interface-marker ", StringComparison.Ordinal)
                      && t.EndsWith("+IProbeSeam", StringComparison.Ordinal))

              let ownerName = marker.Substring("interface-marker ".Length)

              let widened =
                  (sprintf "method %s.Extra(System.Int32) : System.String" ownerName) :: before

              Expect.equal
                  (headline (classify before widened))
                  (Some InterfaceWidening)
                  "a member on an interface the baseline published stops every implementer compiling"

              let fresh =
                  [ "interface-marker Fuaran.Core.Probe.IFresh"
                    "type Fuaran.Core.Probe.IFresh (interface)"
                    "method Fuaran.Core.Probe.IFresh.Extra(System.Int32) : System.String" ]
                  @ before

              Expect.equal
                  (headline (classify before fresh))
                  (Some Additive)
                  "a member on an interface the baseline never published is additive"
          }

          test "ordinary growth is `additive`, and an unmoved surface reports nothing" {
              let before = renderAssembly (Uri(typeof<ProbeRecord>.Assembly.Location).LocalPath)

              Expect.isEmpty (classify before before) "an identical rebuild reports no move at all"

              let grown =
                  [ "type Fuaran.Core.Probe.Helper (module)"
                    "method Fuaran.Core.Probe.Helper.run(System.Int32) : System.Int32" ]
                  @ before

              Expect.equal
                  (headline (classify before grown))
                  (Some Additive)
                  "a new module and a new function on it are additive"

              Expect.isFalse (isBreaking Additive) "only `additive` is safe for a pinned consumer"
          }

          test "an ambiguous overload move is left as removal + addition rather than mis-paired" {
              // Pairing is by IDENTITY, and two overloads share one. Pairing them arbitrarily
              // would report ONE retype where TWO members moved — a smaller change than
              // happened, which is the direction this must not err in.
              let before =
                  [ "type X.T (type)"
                    "method X.T.Go(System.Int32) : System.Int32"
                    "method X.T.Go(System.String) : System.Int32" ]

              let after =
                  [ "type X.T (type)"
                    "method X.T.Go(System.Int64) : System.Int32"
                    "method X.T.Go(System.Double) : System.Int32" ]

              let moves = classify before after
              Expect.equal moves.Length 4 "two removals and two additions, unpaired"
              Expect.isEmpty (moves |> List.filter (fun m -> m.Class = Retype)) "nothing was paired"
              Expect.equal (headline moves) (Some Removal) "the headline is the most severe move present"
          }

          test "the baseline reader drops the header and keeps every token" {
              let rendered =
                  renderBaseline "Fuaran.Core.Probe" [ "type A.B (record)"; "record-field A.B.C #0 : System.Int32" ]

              Expect.isTrue (rendered.EndsWith("\n", StringComparison.Ordinal)) "the file ends with a newline"

              Expect.isFalse
                  (rendered.Contains "\r")
                  "LF only — .gitattributes pins it and WorkingCopyEolTests fails a CRLF working copy"

              Expect.equal
                  (baselineTokens rendered)
                  [ "type A.B (record)"; "record-field A.B.C #0 : System.Int32" ]
                  "the `#` header is not read as surface"

              Expect.isEmpty
                  (baselineTokens "# only a header\n\n   \n")
                  "a header-only file carries no tokens — which is why the completeness leg is a separate check"
          }

          test "identity cuts a token at its signature, ordinal or type" {
              Expect.equal (identity "type A.B (record)") "type A.B" "a type header"
              Expect.equal (identity "record-field A.B.C #3 : System.Int32") "record-field A.B.C" "a record field"
              Expect.equal (identity "union-case A.B.NewC #2(System.Int32)") "union-case A.B.NewC" "a union case"

              Expect.equal
                  (identity "method A.B.Go`1(System.Int32) : System.Int32")
                  "method A.B.Go`1"
                  "a generic method keeps its arity"

              Expect.equal (identity "ctor A.B..ctor(System.Int32)") "ctor A.B..ctor" "a constructor"
              Expect.equal (identity "property A.B.P : System.Int32 { get; set }") "property A.B.P" "a property"
              Expect.equal (identity "field A.B.F : System.Int32 (literal)") "field A.B.F" "a field"
              Expect.equal (identity "interface-marker A.B") "interface-marker A.B" "a marker"
          }

          test "the owner of a member token survives a signature full of dots and spaces" {
              // The go-red for the defect this classifier shipped with for one build: reading
              // the owner off `token.Split(' ')[1]` lands inside a PARAMETER TYPE, and the
              // interface-widening and record-widening classes both key off the owner, so the
              // mis-read reported a breaking move as additive — the one direction that matters.
              Expect.equal
                  (owner "method A.B+IS.Extra(System.Int32, System.String) : System.String")
                  (Some "A.B+IS")
                  "a two-parameter signature does not move the owner"

              Expect.equal
                  (owner "record-field A.B.C #0 : Microsoft.FSharp.Collections.FSharpList`1<System.Int32>")
                  (Some "A.B")
                  "a generic field type does not move the owner"

              Expect.equal
                  (owner "union-case A.B.NewC #1(System.Int32)")
                  (Some "A.B")
                  "a union case's owner is its union"

              Expect.equal (owner "ctor A.B..ctor(System.Int32)") (Some "A.B") "a constructor's owner is its type"

              Expect.equal
                  (owner "type A.B (record)")
                  (Some "A")
                  "a type header has no member, so its `owner` is its namespace"

              Expect.isNone (owner "interface-marker B") "an unqualified marker owns nothing"
          }

          test "the newest tag is chosen by version, not by string order" {
              Expect.equal
                  (newestVersionTag [ "v0.9.0"; "v0.26.0"; "v0.25.0" ])
                  (Some "v0.26.0")
                  "`v0.26.0` is newer than `v0.9.0` — a lexical sort would name the wrong baseline to compare against"

              Expect.equal
                  (newestVersionTag [ "v1.0.0"; "not-a-tag"; "v0.26.0"; "v1.0" ])
                  (Some "v1.0.0")
                  "a tag this does not parse is dropped rather than ordered by accident"

              Expect.isNone (newestVersionTag [ "nightly"; "release/2" ]) "a clone with no `vX.Y.Z` tag yields none"
          } ]
