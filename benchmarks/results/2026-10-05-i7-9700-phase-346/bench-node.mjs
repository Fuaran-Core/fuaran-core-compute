// Phase 346 prototype — the node leg: `node bench-node.mjs [threads ...]` (default 1 2 4 8; 1 is the caller alone through the pool's protocol).
import { Worker } from "node:worker_threads";
import { measure, syncWait } from "./bench-core.mjs";

const threads = process.argv.slice(2).map(Number);
const workerCounts = (threads.length ? threads : [1, 2, 4, 8]).map((t) => t - 1);
let pool = [];

async function spawn(workers, ctrl, buffers) {
  pool = [];
  const ready = [];
  for (let i = 0; i < workers; i++) {
    const w = new Worker(new URL("./worker-node.mjs", import.meta.url), { workerData: { ctrl: ctrl.buffer, buffers } });
    pool.push(w);
    ready.push(new Promise((res, rej) => { w.once("message", res); w.once("error", rej); }));
  }
  await Promise.all(ready);
}

async function terminate() { await Promise.all(pool.map((w) => w.terminate())); pool = []; }

console.log(`# node ${process.version} ${process.arch}`);
await measure({ host: `node ${process.version}`, spawn, wait: syncWait, terminate, workerCounts, log: (l) => console.log(l) });
