// Phase 346 prototype — the browser leg's driver: serve this directory twice on ephemeral loopback
// ports, one origin cross-origin isolated (COOP same-origin + COEP require-corp) and one not, run
// a headless Chromium browser (Chrome or Edge) against each leg in turn, and print the lines each leg posts back.
//
//   node serve.mjs <path to chrome.exe or msedge.exe> [threads, e.g. 1,2,4,8]
import { createServer } from "node:http";
import { readFile, mkdtemp, rm } from "node:fs/promises";
import { spawn } from "node:child_process";
import { tmpdir } from "node:os";
import { join, extname, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const chrome = process.argv[2];
const threads = process.argv[3] || "1,2,4,8";
const types = { ".html": "text/html", ".mjs": "text/javascript", ".js": "text/javascript" };
let pending = null;
const LEG_MS = Number(process.env.LEG_MS || 300_000);
const LEGS = (process.env.LEGS || "probe-plain,probe,main,host").split(",");

function server(isolated) {
  return createServer(async (req, res) => {
    const url = new URL(req.url, "http://x");
    if (process.env.TRACE) console.error(req.method, req.url);
    if (req.method === "POST" && url.pathname === "/result") {
      let body = "";
      for await (const c of req) body += c;
      res.end("ok");
      if (pending) pending(body);
      return;
    }
    const name = url.pathname === "/" ? "browser.html" : url.pathname.slice(1);
    if (name.includes("..") || name.includes("/")) { res.statusCode = 404; return res.end(); }
    try {
      const data = await readFile(join(here, name));
      const headers = { "content-type": types[extname(name)] || "application/octet-stream", "cache-control": "no-store" };
      if (isolated) {
        headers["cross-origin-opener-policy"] = "same-origin";
        headers["cross-origin-embedder-policy"] = "require-corp";
      }
      res.writeHead(200, headers);
      res.end(data);
    } catch { res.statusCode = 404; res.end(); }
  });
}

async function listen(s) { await new Promise((r) => s.listen(0, "127.0.0.1", r)); return s.address().port; }

async function leg(port, mode) {
  const profile = await mkdtemp(join(tmpdir(), "phase346-chrome-"));
  const got = new Promise((res) => { pending = res; });
  const proc = spawn(chrome, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", `--user-data-dir=${profile}`,
    `http://127.0.0.1:${port}/browser.html?mode=${mode}&threads=${threads}`], { stdio: ["ignore", "ignore", "pipe"] });
  let stderr = "";
  proc.on("error", (e) => { stderr += String(e); });
  proc.stderr.on("data", (c) => { stderr += c; });
  const timeout = new Promise((res) => setTimeout(() => res(JSON.stringify({ error: `leg ${mode} timed out`, chromeStderr: stderr.slice(-2000) })), LEG_MS));
  const body = await Promise.race([got, timeout]);
  proc.kill();
  await new Promise((r) => setTimeout(r, 1000));
  await rm(profile, { recursive: true, force: true }).catch(() => {});
  return body;
}

const iso = server(true), plain = server(false);
const isoPort = await listen(iso), plainPort = await listen(plain);
const legs = {
  "probe-plain": ["# not isolated, probe", plainPort, "probe"],
  probe: ["# isolated, probe", isoPort, "probe"],
  main: ["# isolated, the evaluator on the page's thread (Atomics.waitAsync)", isoPort, "main"],
  host: ["# isolated, the evaluator in a dedicated worker (Atomics.wait)", isoPort, "host"],
};
for (const name of LEGS) {
  const [title, port, mode] = legs[name];
  console.log(title);
  console.log(await leg(port, mode));
}
iso.close(); plain.close();
