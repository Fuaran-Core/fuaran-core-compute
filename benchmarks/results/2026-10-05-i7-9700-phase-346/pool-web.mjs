// Phase 346 prototype — the browser's pool, used from the page's main thread and from a host worker.
let pool = [];

export async function spawn(workers, ctrl, buffers) {
  pool = [];
  const ready = [];
  for (let i = 0; i < workers; i++) {
    const w = new Worker(new URL("./worker-web.mjs", import.meta.url), { type: "module" });
    pool.push(w);
    ready.push(new Promise((res, rej) => { w.onmessage = res; w.onerror = (e) => rej(new Error(e.message)); }));
    w.postMessage({ ctrl: ctrl.buffer, buffers });
  }
  await Promise.all(ready);
}

export async function terminate() { for (const w of pool) w.terminate(); pool = []; }

// What the page can do: is it isolated, does it have shared memory, may its thread block?
export function probe() {
  let blocking;
  try {
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 0);
    blocking = "Atomics.wait allowed";
  } catch (e) {
    blocking = `Atomics.wait refused: ${e.name}: ${e.message}`;
  }
  return {
    crossOriginIsolated: globalThis.crossOriginIsolated === true,
    sharedArrayBuffer: typeof SharedArrayBuffer,
    waitAsync: typeof Atomics.waitAsync,
    blocking: typeof SharedArrayBuffer === "function" ? blocking : "no SharedArrayBuffer to wait on",
    cores: navigator.hardwareConcurrency,
    userAgent: navigator.userAgent,
  };
}
