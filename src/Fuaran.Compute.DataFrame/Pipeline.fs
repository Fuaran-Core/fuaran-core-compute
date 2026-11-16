namespace Fuaran.Compute

open Fuaran.Core

// ============================================================================
//  Phase 604 (`DECISIONS.md` D163) — the pipeline hash, a registered minting
//  rule rather than a convention one repository keeps.
//
//  A pipeline's canonical bytes are `DataFrameCodec.encodePipeline`'s; the
//  pipeline wire specification states them, and the rule below, normatively.
//  `compute-pipeline-sha256-v1` digests those bytes, UTF-8, with SHA-256 and
//  writes the content address `sha256:<64 lowercase hex>`. The identifier names
//  the format major: a later format that moves the bytes mints under `-v2`, and
//  a `-v1` hash stays valid and stays distinguishable, because the identifier
//  travels beside the hash.
//
//  Only a pipeline whose canonical text the guarded writer accepts is mintable:
//  a non-finite float, an ill-formed string or a repeated member has no
//  canonical bytes of its own (two values would share them, or the text would
//  not read back), so `hash` refuses it rather than digest an alias.
//
//  After Incremental.fs, because the digest is the package's one SHA-256
//  (`StateWire.sha256Hex`, Fable-clean): the incremental state's pipeline key is
//  the same digest over the same bytes, written bare.
// ============================================================================

/// The pipeline hash (Phase 604, `DECISIONS.md` D163): the minting rule `compute-pipeline-sha256-v1`
/// over a pipeline's canonical bytes, as the pipeline wire specification states it.
[<RequireQualifiedAccess>]
module Pipeline =

    /// The minting rule's identifier. It names the format major: a breaking change to the canonical
    /// bytes mints under a new identifier, and a hash minted under this one stays valid.
    [<Literal>]
    let hashAlgorithm = "compute-pipeline-sha256-v1"

    /// The canonical text of `pipeline` — the bytes the hash is computed over — or the refusal of a
    /// pipeline that has none: one carrying a non-finite float, an ill-formed string or a repeated
    /// member name, named with its path. Over a mintable pipeline it is `DataFrameCodec.encodePipeline`.
    let canonicalText (pipeline: Transform list) : Result<string, string> =
        Canonical.tryWrite Rendering.CrossHost (JArr(pipeline |> List.map DataFrameCodec.encodeTransform))

    /// The pipeline hash: `sha256:` and the lowercase hex SHA-256 of the UTF-8 bytes of the canonical
    /// text, or the refusal `canonicalText` names. Two pipelines with one canonical text have one hash,
    /// whatever their history: the lowered `Transform list` carries no node ids and no authoring order
    /// beyond the steps' own.
    let hash (pipeline: Transform list) : Result<string, string> =
        canonicalText pipeline
        |> Result.map (fun text -> "sha256:" + StateWire.sha256Hex text)
