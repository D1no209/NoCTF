import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const receipt = process.argv[2];
if (!receipt) throw new Error("A unique startup receipt path is required.");
process.argv.splice(1, 2, fileURLToPath(new URL("./workload.mjs", import.meta.url)));
// No business module or HTTP request can execute before this receipt exists.
writeFileSync(receipt, JSON.stringify({ pid: process.pid, node: process.version,
  startedAt: new Date().toISOString() }), { flag: "wx", mode: 0o600 });
await import("./workload.mjs");
