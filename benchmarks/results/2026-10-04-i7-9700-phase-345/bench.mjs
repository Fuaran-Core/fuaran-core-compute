// Phase 345 evidence — node leg. Same algorithm (CPython math.fsum) over the same bytes the .NET leg wrote.
import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";

function fsum(xs) {
  const partials = [];
  let count = 0;
  for (let k = 0; k < xs.length; k++) {
    let x = xs[k];
    let i = 0;
    for (let j = 0; j < count; j++) {
      let y = partials[j];
      if (Math.abs(x) < Math.abs(y)) { const t = x; x = y; y = t; }
      const hi = x + y;
      const lo = y - (hi - x);
      if (lo !== 0.0) { partials[i++] = lo; }
      x = hi;
    }
    partials[i] = x;
    count = i + 1;
  }
  let hi = 0.0;
  if (count > 0) {
    let n = count - 1;
    hi = partials[n];
    let lo = 0.0;
    while (n > 0) {
      const x = hi;
      const y = partials[--n];
      hi = x + y;
      const yr = hi - x;
      lo = y - yr;
      if (lo !== 0.0) break;
    }
    if (n > 0 && ((lo < 0 && partials[n - 1] < 0) || (lo > 0 && partials[n - 1] > 0))) {
      const y = lo * 2;
      const x = hi + y;
      const yr = x - hi;
      if (y === yr) hi = x;
    }
  }
  return hi;
}

function fold(xs) { let t = 0.0; for (let i = 0; i < xs.length; i++) t += xs[i]; return t; }

function bits(v) { const b = new DataView(new ArrayBuffer(8)); b.setFloat64(0, v, true); return b.getBigUint64(0, true).toString(16).padStart(16, "0"); }

function time(reps, f) {
  let sink = 0;
  for (let i = 0; i < 3; i++) sink += f();
  const ts = [];
  for (let i = 0; i < reps; i++) { const t0 = process.hrtime.bigint(); sink += f(); ts.push(Number(process.hrtime.bigint() - t0) / 1e6); }
  ts.sort((a, b) => a - b);
  globalThis.__sink = sink;
  return ts[ts.length >> 1];
}

const dir = process.argv[2];
const out = [];
console.log(`node ${process.version} ${process.arch}`);
console.log("| dataset | n | fold ms | fsum ms | fsum/fold | fold bits == fsum bits |");
console.log("|---|---|---|---|---|---|");
for (const kind of ["quarters", "uniform", "wide"]) {
  for (const n of [10000, 100000, 1000000]) {
    const buf = readFileSync(join(dir, `${kind}-${n}.f64`));
    const xs = new Float64Array(buf.buffer, buf.byteOffset, n);
    const reps = n === 1000000 ? 15 : n === 100000 ? 51 : 201;
    const tFold = time(reps, () => fold(xs));
    const tFsum = time(reps, () => fsum(xs));
    const vFold = fold(xs), vFsum = fsum(xs);
    console.log(`| ${kind} | ${n} | ${tFold.toFixed(3)} | ${tFsum.toFixed(3)} | ${(tFsum / tFold).toFixed(1)}x | ${bits(vFold) === bits(vFsum)} |`);
    out.push(`${kind} ${n} fsum=${bits(vFsum)} fold=${bits(vFold)}`);
  }
}
console.log("");
for (const l of out) console.log(l);
writeFileSync(join(dir, "node-results.txt"), out.join("\n") + "\n");
