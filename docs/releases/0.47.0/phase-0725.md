### Sine, cosine and the two arc tangents are pinned in `Fuaran.Compute.Elementary` (Phase 725, `DECISIONS.md` D84) — additive

**What changed.** The `Elementary` module gains `sin`, `cos`, `sinCos` (the pair from one reduction, as a struct
tuple), `atan` and `atan2`, and the four tables that answer first, `sinTable`, `cosTable`, `atanTable` and
`atan2Table`, each with a `Pinned` row in `ElementaryOperations.exactness`. `ElementaryLaws.laws` gains four laws. The
package's baseline, `api/Fuaran.Compute.Elementary.txt`, gains those nine functions and loses nothing; no other file
under `api/` or `api/wire/` moves. Nothing that was published answers another bit: `Special`'s reflections now read
their sine and cosine kernels from `Elementary`, where `sin` and `cos` read them too, and the kernels under a zero
tail are the expressions they were (D84 item 4).

**The wire.** None. The package encodes nothing.

**The law vectors.** `conformance/laws/elementary-laws.json` gains 190 vectors between `pow`'s and the generator's, and
its description says so. At some of them (`*-not-correctly-rounded-*`) the pinned answer is one double from the
correctly rounded one, so a port that answers any of the four functions correctly rounded fails. Seven vectors of the
same kind join the `exp`, `ln` and `log10` vectors, each after its function's last, so a correctly rounded `exp`,
`ln`, `log10` or `pow` fails one too. Every vector that was there is the line it was, byte for byte, the 309 special-function
vectors among them, which the suite now pins by digest. Re-emitted at `0.47.0`, `transform-laws.json` moves only in its
`kitVersion` stamp, and every other file under `conformance/laws/` is byte-identical.

**What a consumer must do.** Nothing. A caller that wants a trigonometric function opens `Fuaran.Compute.Elementary`
and writes `Elementary.sin`. A host that ports the functions certifies against the new vectors, each of which states
the function's bits and, where the routine computed them, its distance from a fifty-digit reference. The argument is
reduced exactly at every finite double, so there is no bound past which the answer degrades (D84 item 3). The accuracy
is a measurement, stated by range in D84 item 9: every function was within one unit in the last place of the true value
over the 55,998-value sample, at every magnitude. A wider search, of hundreds of millions of inputs where each
function's error is largest, found `sin`, `cos`, `atan` and `atan2` no more than 0.953, 0.952, 0.854 and 0.924 units
away: none a whole unit, which is still a measurement and not a bound. `atan2` forms its three middle arms from its two
operands for that reason (D84 item 7): formed from the rounded quotient they passed the sample and were 1.02 units away
at their worst input. The evaluator's scalar set gains nothing: a pipeline cannot yet ask for a sine.
