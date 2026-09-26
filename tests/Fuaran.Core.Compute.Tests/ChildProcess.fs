module Fuaran.Core.Tests.ChildProcess

open System.Diagnostics
open System.Text

// ---------------------------------------------------------------------------
// Spawning a child whose stdout the cross-host suites compare byte-for-byte.
//
// `Process.StandardOutput` decodes the pipe with the PARENT's
// `Console.OutputEncoding` unless told otherwise. On Windows that is the
// console's code page, so under an OEM page (CP850) the child's UTF-8 `é`
// (C3 A9) decodes to `├⌐` and every vector carrying a non-ASCII character
// diverges from the in-process interpreter leg.
//
// That made the green gate a function of the console the runner happened to
// inherit rather than of the code: the Phase 316 and Phase 698 sweeps passed
// under `dotnet run` and failed when the built test dll was invoked directly.
//
// Both children the suite spawns — `node` and `dotnet fsi` — write UTF-8 to a
// redirected stream, so the decode is pinned here instead of left ambient.
// Every redirected spawn goes through this one function deliberately: a site
// that omits the setting still passes on the machine that wrote it, so the
// omission is invisible exactly where it would be caught.
// ---------------------------------------------------------------------------

let private utf8 = UTF8Encoding false

/// A `ProcessStartInfo` with both output streams redirected and their decoding
/// pinned to UTF-8, independent of the console's code page.
let redirected (fileName: string) (arguments: string) =
    let psi = ProcessStartInfo(fileName, arguments)
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.StandardOutputEncoding <- utf8
    psi.StandardErrorEncoding <- utf8
    psi
