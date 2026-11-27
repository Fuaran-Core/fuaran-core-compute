# Fuaran.Core.Compute — the release record

What each version changed, by class: a directory per version, a file per change. The contract every
version is held to (the versioning policy, the public-surface baselines) is [`STABILITY.md`](../../STABILITY.md);
this directory is its record. The layout is `DECISIONS.md` D91 (Phase 734). The versions before `0.33.0` shipped
from the Fuaran.Core repository and are recorded in
[that repository's release ledger](https://github.com/Fuaran-Core/fuaran-core/blob/main/docs/releases/README.md).

- **`<version>/README.md`** is the version's own record. Its first line is its heading, one of
  `## <version> — DRAFT` (the standing `<Version>`, untagged, and only it),
  ``## <version> — released <date> as `v<version>` `` or
  ``## <version> — never released; its entries ship in `<later version>` ``; then the preamble: why the slot
  opened, and, once it is released, its release record.
- **`<version>/phase-<n>.md`** is one change, the entry of the phase numbered `<n>` in this repository's
  roadmap record, `<n>` zero-padded to four digits so a listing reads in phase order. It holds the entry's
  `### <what changed> (Phase <n>, …) — <class>` heading and its body. A change no phase of that record made
  (a substrate raise, another repository's phase) is **`<version>/change-<slug>.md`**, the slug lowercase
  letters, digits, `.` and `-`.
- **A change adds its file to the standing `<Version>`'s directory and edits nothing else here.** The change
  that opens a slot (the first after a tag) also writes that slot's `README.md`. Two changes in flight never
  edit one file.
- **At a release** the release commit re-heads the slot's `README.md` `released <date> as v<version>` and
  adds its release record there. No entry file moves or is rewritten.
- **An untagged draft that advances** (a change of a higher class than the draft carries) moves its directory
  to the new number, its entries with it, and re-heads it. The number it leaves keeps a `README.md` headed
  `never released` only where packages were packed at it (`0.38.0`'s case), and that slot holds no entry. An
  entry a branch in flight still adds under the old number then lands in a slot with no heading, or in a
  never-released one, and the roster refuses either by name.

The `Package roster` family holds the record to `<Version>` and to the tags: the standing version has a slot;
every release tag from `0.33.0` up has one; a DRAFT heading is the standing version's and is untagged; a
released heading has its tag; every file in a slot is an entry, and a never-released slot holds none; and
`STABILITY.md` holds no release heading.
