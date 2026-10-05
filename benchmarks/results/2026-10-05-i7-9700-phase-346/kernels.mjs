// Phase 346 prototype — the two row-local steps the morsel runner carries, as plain loops over typed
// arrays, one morsel ([lo, hi)) at a time. The SAME functions run on the caller's thread (the
// sequential arm) and inside every worker (the worker arm), so the difference between the arms is the
// threading and nothing else. They are hand loops in the shape Phase 326 made the emitted code take
// (direct indexing, int32 arithmetic carried in float64 with the overflow test), not the Fable-compiled
// evaluator: the evaluator's own sequential figures are taken separately (seq/), for scale.
//
//   filter — Layer 6's `compiled` case: `a + b > 500` over two int columns with validity masks. Each
//            morsel writes the physical rows it keeps at its own offset of `sel` and its count at
//            `counts[j]`; the caller compacts the morsels' rows in morsel order (the evaluator's
//            `Array.concat kept`).
//   derive — the sheet's `lines` node: `amount = qty * price` (int * float), then
//            `big = amount >= threshold`. Each morsel writes its rows of the two new vectors and
//            their masks; morsels write disjoint positions.

export const MORSEL_ROWS = 8192;

export function morselCount(n) {
  return n <= 0 ? 0 : ((n + MORSEL_ROWS - 1) / MORSEL_ROWS) | 0;
}

// The columns seq/Program.fs builds: Layer 6's `rowTable` with its products taken exactly (they reach
// 1.05e11, exact in a double), and the corpus's `orders`.
export function fillRows(n, a, am, b, bm) {
  for (let i = 0; i < n; i++) {
    a[i] = (i * 7919) % 1000;
    b[i] = (i * 104729) % 1000;
    am[i] = 1;
    bm[i] = 1;
  }
}

export function fillOrders(n, qty, qm, price, pm) {
  for (let i = 0; i < n; i++) {
    qty[i] = 1 + ((i * 7 + ((i / 3) | 0)) % 20);
    price[i] = (4 + ((i * 13) % 397)) * 0.25;
    qm[i] = 1;
    pm[i] = 1;
  }
}

// The int add as Phase 326 emits it: carried in float64, refused on leaving int32.
function addInt(x, y) {
  const s = x + y;
  if (s > 2147483647 || s < -2147483648) throw new Error("Arithmetic operation resulted in an overflow.");
  return s;
}

export function filterMorsel(j, n, a, am, b, bm, sel, counts) {
  const lo = j * MORSEL_ROWS;
  const hi = Math.min(n, lo + MORSEL_ROWS);
  let c = 0;
  for (let p = lo; p < hi; p++) {
    if (am[p] !== 0 && bm[p] !== 0 && addInt(a[p], b[p]) > 500) {
      sel[lo + c] = p;
      c++;
    }
  }
  counts[j] = c;
}

export function deriveMorsel(j, n, qty, qm, price, pm, threshold, amount, amountMask, big, bigMask) {
  const lo = j * MORSEL_ROWS;
  const hi = Math.min(n, lo + MORSEL_ROWS);
  for (let p = lo; p < hi; p++) {
    if (qm[p] !== 0 && pm[p] !== 0) {
      const v = qty[p] * price[p];
      amount[p] = v;
      amountMask[p] = 1;
      big[p] = v >= threshold ? 1 : 0;
      bigMask[p] = 1;
    } else {
      amountMask[p] = 0;
      bigMask[p] = 0;
    }
  }
}

// The caller's compaction of the morsels' kept rows, in morsel order.
export function compact(m, sel, counts) {
  let w = 0;
  for (let j = 0; j < m; j++) {
    const lo = j * MORSEL_ROWS;
    const c = counts[j];
    if (w !== lo) sel.copyWithin(w, lo, lo + c);
    w += c;
  }
  return w;
}

// ---- the worker side of the pool --------------------------------------------------------------
//
// ctrl (Int32Array over a SharedArrayBuffer):
//   [0] generation — bumped by the caller to start a run; workers Atomics.wait on it
//   [1] next morsel — claimed by Atomics.add, by workers and the caller alike
//   [2] workers still running this generation
//   [3] case (1 = filter, 2 = derive), [4] n, [5] morsels
//   [6] the generation the last worker finished — the caller waits on it
//   [7] stop flag

export const C_GEN = 0, C_NEXT = 1, C_LEFT = 2, C_CASE = 3, C_N = 4, C_M = 5, C_DONE = 6, C_STOP = 7;

export function bindViews(buffers) {
  return {
    a: new Int32Array(buffers.a), am: new Uint8Array(buffers.am),
    b: new Int32Array(buffers.b), bm: new Uint8Array(buffers.bm),
    sel: new Int32Array(buffers.sel), counts: new Int32Array(buffers.counts),
    qty: new Int32Array(buffers.qty), qm: new Uint8Array(buffers.qm),
    price: new Float64Array(buffers.price), pm: new Uint8Array(buffers.pm),
    amount: new Float64Array(buffers.amount), amountMask: new Uint8Array(buffers.amountMask),
    big: new Uint8Array(buffers.big), bigMask: new Uint8Array(buffers.bigMask),
    threshold: buffers.threshold,
  };
}

// Claim morsels until none are left. Used by every worker and by the caller.
export function drain(ctrl, v) {
  const kind = ctrl[C_CASE], n = ctrl[C_N], m = ctrl[C_M];
  for (;;) {
    const j = Atomics.add(ctrl, C_NEXT, 1);
    if (j >= m) return;
    if (kind === 1) filterMorsel(j, n, v.a, v.am, v.b, v.bm, v.sel, v.counts);
    else deriveMorsel(j, n, v.qty, v.qm, v.price, v.pm, v.threshold, v.amount, v.amountMask, v.big, v.bigMask);
  }
}

// A worker's loop: wait for a generation, drain, report; until the stop flag.
export function workerLoop(ctrl, getViews) {
  let seen = Atomics.load(ctrl, C_GEN);
  for (;;) {
    Atomics.wait(ctrl, C_GEN, seen);
    if (Atomics.load(ctrl, C_STOP) !== 0) return;
    const gen = Atomics.load(ctrl, C_GEN);
    if (gen === seen) continue;
    seen = gen;
    drain(ctrl, getViews());
    if (Atomics.sub(ctrl, C_LEFT, 1) === 1) {
      Atomics.store(ctrl, C_DONE, gen);
      Atomics.notify(ctrl, C_DONE);
    }
  }
}
