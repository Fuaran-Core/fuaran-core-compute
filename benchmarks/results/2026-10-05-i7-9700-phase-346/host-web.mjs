// Phase 346 prototype — the evaluator hosted in a dedicated worker: the only place in a browser a
// synchronous kernel member could wait for its pool (Atomics.wait is refused on the page's thread).
import { measure, syncWait } from "./bench-core.mjs";
import { spawn, terminate, probe } from "./pool-web.mjs";

self.onmessage = async (e) => {
  const lines = [];
  try {
    const p = probe();
    lines.push(JSON.stringify({ probe: "host-worker", ...p }));
    await measure({ host: "browser/host-worker", spawn, wait: syncWait, terminate, workerCounts: e.data.workerCounts, log: (l) => lines.push(l) });
  } catch (err) {
    lines.push(JSON.stringify({ error: String(err && err.stack || err) }));
  }
  self.postMessage(lines);
};
