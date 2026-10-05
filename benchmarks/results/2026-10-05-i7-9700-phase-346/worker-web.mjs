// Phase 346 prototype — a browser worker of the pool (a module worker): the node worker's body
// behind the message channel a Web Worker has.
import { bindViews, workerLoop } from "./kernels.mjs";

self.onmessage = (e) => {
  const views = bindViews(e.data.buffers);
  const ctrl = new Int32Array(e.data.ctrl);
  self.postMessage("ready");
  workerLoop(ctrl, () => views);
};
