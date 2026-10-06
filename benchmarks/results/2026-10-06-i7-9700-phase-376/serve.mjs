// Phase 376 — the browser legs' driver: serve the Fable output root twice on ephemeral loopback ports,
// one origin cross-origin isolated (COOP same-origin + COEP require-corp) and one not, run a headless
// Chromium browser (Edge or Chrome) against each leg in turn, and print the lines each leg posts back.
//
//   node serve.mjs <Fable output root> <path to msedge.exe or chrome.exe> [sizes, e.g. 10000,100000]
//
// The output root is the directory `dotnet fable poolbench.fsproj -o <root>/benchmarks/results/
// 2026-10-06-i7-9700-phase-376` mirrored the repository into; index.html beside this file is served
// at its top.
import { createServer } from "node:http";
import { readFile, mkdtemp, rm } from "node:fs/promises";
import { spawn } from "node:child_process";
import { tmpdir } from "node:os";
import { join, extname, dirname, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(process.argv[2]);
const browser = process.argv[3];
const sizes = process.argv[4] || "";
const types = { ".html": "text/html", ".mjs": "text/javascript", ".js": "text/javascript" };
const LEG_MS = Number(process.env.LEG_MS || 900_000);
const LEGS = (process.env.LEGS || "probe-plain,probe,main,host").split(",");
let pending = null;

function server(isolated) {
  return createServer(async (req, res) => {
    const url = new URL(req.url, "http://x");
    if (req.method === "POST" && url.pathname === "/result") {
      let body = "";
      for await (const c of req) body += c;
      res.end("ok");
      if (pending) pending(body);
      return;
    }
    const file = url.pathname === "/index.html" ? join(here, "index.html") : resolve(root, "." + decodeURIComponent(url.pathname));
    if (file !== join(here, "index.html") && !file.startsWith(root + sep)) { res.statusCode = 404; return res.end(); }
    try {
      const data = await readFile(file);
      const headers = { "content-type": types[extname(file)] || "application/octet-stream", "cache-control": "no-store" };
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
  const profile = await mkdtemp(join(tmpdir(), "phase376-browser-"));
  const got = new Promise((res) => { pending = res; });
  const proc = spawn(browser, ["--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", `--user-data-dir=${profile}`,
    `http://127.0.0.1:${port}/index.html?mode=${mode}&sizes=${sizes}`], { stdio: ["ignore", "ignore", "pipe"] });
  let stderr = "";
  proc.on("error", (e) => { stderr += String(e); });
  proc.stderr.on("data", (c) => { stderr += c; });
  let timer;
  const timeout = new Promise((res) => { timer = setTimeout(() => res(`ERROR leg ${mode} timed out\n${stderr.slice(-2000)}`), LEG_MS); });
  const body = await Promise.race([got, timeout]);
  clearTimeout(timer);
  proc.kill();
  await new Promise((r) => setTimeout(r, 1000));
  await rm(profile, { recursive: true, force: true }).catch(() => {});
  return body;
}

const iso = server(true), plain = server(false);
const isoPort = await listen(iso), plainPort = await listen(plain);
const legs = {
  "probe-plain": ["# not isolated: the opt-in", plainPort, "probe"],
  probe: ["# isolated: the opt-in", isoPort, "probe"],
  main: ["# isolated, the evaluator on the page's thread", isoPort, "main"],
  host: ["# isolated, the evaluator in a dedicated worker", isoPort, "host"],
};
for (const name of LEGS) {
  const [title, port, mode] = legs[name];
  console.log(title);
  console.log(await leg(port, mode));
}
iso.close(); plain.close();
