// Phase 346 prototype — what a worker pays to load the EVALUATOR, not the prototype's few loops: a
// worker of a real runner would have to import the Fable-compiled DataFrame module to compile a step
// from data. `node startup-bundle.mjs <Fable output>/src/Fuaran.Compute.DataFrame/DataFrame.js`
// spawns 1, 3 and 7 workers that import it and report ready, five times each, cold each time.
import { Worker } from "node:worker_threads";
import { pathToFileURL } from "node:url";

const target = pathToFileURL(process.argv[2]).href;
const body = `import(${JSON.stringify(target)}).then(() => require("node:worker_threads").parentPort.postMessage("ready"));`;

async function spawn(k) {
  const t0 = performance.now();
  const ws = [];
  await Promise.all(Array.from({ length: k }, () => new Promise((res, rej) => {
    const w = new Worker(body, { eval: true });
    ws.push(w);
    w.once("message", res);
    w.once("error", rej);
  })));
  const ms = performance.now() - t0;
  await Promise.all(ws.map((w) => w.terminate()));
  return ms;
}

for (const k of [1, 3, 7]) {
  const ts = [];
  for (let i = 0; i < 5; i++) ts.push(await spawn(k));
  ts.sort((a, b) => a - b);
  console.log(JSON.stringify({ host: `node ${process.version}`, arm: "startup-evaluator", workers: k, ms: ts[2], all: ts.map((t) => +t.toFixed(1)) }));
}
