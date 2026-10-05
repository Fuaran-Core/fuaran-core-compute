// Phase 346 prototype — a node worker of the pool: bind the shared buffers once, say so, then wait
// for generations until the stop flag.
import { workerData, parentPort } from "node:worker_threads";
import { bindViews, workerLoop } from "./kernels.mjs";

const views = bindViews(workerData.buffers);
const ctrl = new Int32Array(workerData.ctrl);
parentPort.postMessage("ready");
workerLoop(ctrl, () => views);
