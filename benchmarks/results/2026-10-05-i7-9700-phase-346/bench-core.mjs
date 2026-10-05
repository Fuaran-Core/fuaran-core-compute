// Phase 346 prototype — the measurement, shared by the node leg (bench-node.mjs) and the browser
// legs (browser.mjs on the page's main thread, host-web.mjs inside a dedicated worker).
//
// The host supplies `spawn(workers, ctrl, buffers)` — start that many workers, hand each the control
// block and the shared buffers, resolve when every one has said it is ready — and `wait(ctrl, gen)`,
// which returns when the generation has finished: synchronously (Atomics.wait — node, or any worker)
// or as a promise (Atomics.waitAsync — the page's main thread, where Atomics.wait throws).

import {
  MORSEL_ROWS, morselCount, fillRows, fillOrders, filterMorsel, deriveMorsel, compact, bindViews, drain,
  C_GEN, C_NEXT, C_LEFT, C_CASE, C_N, C_M, C_DONE, C_STOP,
} from "./kernels.mjs";

export const SIZES = [10_000, 100_000, 1_000_000];
export const THRESHOLD = 500.0;
const MAX = 1_000_000;

function reps(n) { return n >= 1_000_000 ? 15 : n >= 100_000 ? 51 : 201; }

function median(xs) { const s = xs.slice().sort((a, b) => a - b); return s[s.length >> 1]; }

// Every buffer one evaluation reads or writes, sized for the largest case; a smaller case uses the
// prefix (every column is a function of the row's position, so the prefix IS the smaller table).
function allocate(Buf) {
  const bytes = { a: 4, am: 1, b: 4, bm: 1, sel: 4, counts: 4, qty: 4, qm: 1, price: 8, pm: 1, amount: 8, amountMask: 1, big: 1, bigMask: 1 };
  const buffers = { threshold: THRESHOLD };
  for (const [k, w] of Object.entries(bytes)) {
    const len = k === "counts" ? morselCount(MAX) : MAX;
    buffers[k] = new Buf(len * w);
  }
  return buffers;
}

function fill(v) {
  fillRows(MAX, v.a, v.am, v.b, v.bm);
  fillOrders(MAX, v.qty, v.qm, v.price, v.pm);
}

// The sequential member: one thread, morsel by morsel, no atomics.
function sequential(kind, n, v) {
  const m = morselCount(n);
  if (kind === 1) {
    for (let j = 0; j < m; j++) filterMorsel(j, n, v.a, v.am, v.b, v.bm, v.sel, v.counts);
    return compact(m, v.sel, v.counts);
  }
  for (let j = 0; j < m; j++) deriveMorsel(j, n, v.qty, v.qm, v.price, v.pm, v.threshold, v.amount, v.amountMask, v.big, v.bigMask);
  return n;
}

// The answer's bytes, for the byte-identity check.
function answer(kind, n, v, kept) {
  if (kind === 1) return new Uint8Array(v.sel.buffer, 0, kept * 4).slice();
  const out = new Uint8Array(n * 8 + n * 3);
  out.set(new Uint8Array(v.amount.buffer, 0, n * 8), 0);
  out.set(v.amountMask.subarray(0, n), n * 8);
  out.set(v.big.subarray(0, n), n * 9);
  out.set(v.bigMask.subarray(0, n), n * 10);
  return out;
}

function equalBytes(x, y) {
  if (x.length !== y.length) return false;
  for (let i = 0; i < x.length; i++) if (x[i] !== y[i]) return false;
  return true;
}

function clearOutputs(v) { v.sel.fill(0); v.counts.fill(0); v.amount.fill(0); v.amountMask.fill(0); v.big.fill(0); v.bigMask.fill(0); }

const now = () => performance.now();

// One run on the pool: publish the step, wake the workers, drain beside them, wait for the last.
async function pooled(ctrl, workers, kind, n, v, wait) {
  const m = morselCount(n);
  ctrl[C_CASE] = kind; ctrl[C_N] = n; ctrl[C_M] = m;
  Atomics.store(ctrl, C_NEXT, 0);
  Atomics.store(ctrl, C_LEFT, workers);
  const gen = Atomics.add(ctrl, C_GEN, 1) + 1;
  Atomics.notify(ctrl, C_GEN);
  drain(ctrl, v);
  if (workers > 0) {
    const r = wait(ctrl, gen);
    if (r && typeof r.then === "function") await r;
  }
  return kind === 1 ? compact(m, v.sel, v.counts) : n;
}

export function syncWait(ctrl, gen) {
  let d;
  while ((d = Atomics.load(ctrl, C_DONE)) !== gen) Atomics.wait(ctrl, C_DONE, d);
}

export async function asyncWait(ctrl, gen) {
  let d;
  while ((d = Atomics.load(ctrl, C_DONE)) !== gen) {
    const r = Atomics.waitAsync(ctrl, C_DONE, d);
    if (r.async) await r.value;
  }
}

// The whole measurement. `log` receives one line per result; the return is the rows, for a host
// that ships them elsewhere (the browser legs post them to the server).
export async function measure({ host, spawn, wait, terminate, workerCounts, log }) {
  const rows = [];
  const out = (r) => { rows.push(r); log(JSON.stringify(r)); };

  // 1. The sequential member over ordinary buffers, and over shared ones: a frame whose vectors live
  //    in shared memory pays whatever the engine charges for that on EVERY run, threaded or not.
  const plain = bindViews(allocate(ArrayBuffer));
  fill(plain);
  const sharedBuffers = allocate(SharedArrayBuffer);
  const shared = bindViews(sharedBuffers);
  fill(shared);

  const reference = {};
  for (const kind of [1, 2]) {
    for (const n of SIZES) {
      for (const [mem, v] of [["plain", plain], ["shared", shared]]) {
        clearOutputs(v);
        let kept = 0;
        for (let i = 0; i < 3; i++) kept = sequential(kind, n, v);
        const ts = [];
        for (let i = 0; i < reps(n); i++) { const t0 = now(); kept = sequential(kind, n, v); ts.push(now() - t0); }
        const bytes = answer(kind, n, v, kept);
        if (mem === "plain") reference[`${kind}/${n}`] = bytes;
        else if (!equalBytes(bytes, reference[`${kind}/${n}`])) throw new Error(`sequential over shared memory disagrees: ${kind}/${n}`);
        out({ host, step: kind === 1 ? "filter" : "derive", rows: n, arm: `sequential/${mem}`, threads: 1, ms: median(ts), kept });
      }
    }
  }

  // 2. Transfer: what moving a frame's inputs into shared memory costs when they were not allocated
  //    there, and moving a result out again. One copy each, the columns the step reads or writes.
  for (const n of SIZES) {
    const t = [];
    for (let i = 0; i < reps(n); i++) {
      const t0 = now();
      shared.a.set(plain.a.subarray(0, n)); shared.am.set(plain.am.subarray(0, n));
      shared.b.set(plain.b.subarray(0, n)); shared.bm.set(plain.bm.subarray(0, n));
      t.push(now() - t0);
    }
    out({ host, step: "filter", rows: n, arm: "copy-in", threads: 1, ms: median(t) });
    const u = [];
    for (let i = 0; i < reps(n); i++) {
      const t0 = now();
      shared.qty.set(plain.qty.subarray(0, n)); shared.qm.set(plain.qm.subarray(0, n));
      shared.price.set(plain.price.subarray(0, n)); shared.pm.set(plain.pm.subarray(0, n));
      u.push(now() - t0);
    }
    out({ host, step: "derive", rows: n, arm: "copy-in", threads: 1, ms: median(u) });
    const w = [];
    for (let i = 0; i < reps(n); i++) {
      const t0 = now();
      const amount = shared.amount.slice(0, n), am = shared.amountMask.slice(0, n), big = shared.big.slice(0, n), bm = shared.bigMask.slice(0, n);
      w.push(now() - t0);
      globalThis.__sink = amount.length + am.length + big.length + bm.length;
    }
    out({ host, step: "derive", rows: n, arm: "copy-out", threads: 1, ms: median(w) });
  }

  // 3. The pool: cold start-up, then warm runs, each answer checked against the sequential bytes.
  for (const workers of workerCounts) {
    const ctrl = new Int32Array(new SharedArrayBuffer(64));
    const t0 = now();
    if (workers > 0) await spawn(workers, ctrl, sharedBuffers);
    const startup = now() - t0;
    if (workers > 0) out({ host, step: "-", rows: 0, arm: "startup", threads: workers + 1, ms: startup });
    for (const kind of [1, 2]) {
      for (const n of SIZES) {
        clearOutputs(shared);
        let kept = 0;
        for (let i = 0; i < 3; i++) kept = await pooled(ctrl, workers, kind, n, shared, wait);
        const ts = [];
        for (let i = 0; i < reps(n); i++) { const t1 = now(); kept = await pooled(ctrl, workers, kind, n, shared, wait); ts.push(now() - t1); }
        const same = equalBytes(answer(kind, n, shared, kept), reference[`${kind}/${n}`]);
        if (!same) throw new Error(`the pool disagrees with the sequential member: ${kind}/${n} at ${workers + 1} threads`);
        out({ host, step: kind === 1 ? "filter" : "derive", rows: n, arm: "pool", threads: workers + 1, ms: median(ts), kept, identical: same });
      }
    }
    Atomics.store(ctrl, C_STOP, 1);
    Atomics.add(ctrl, C_GEN, 1);
    Atomics.notify(ctrl, C_GEN);
    if (workers > 0) await terminate();
  }
  return rows;
}

export { MORSEL_ROWS };
