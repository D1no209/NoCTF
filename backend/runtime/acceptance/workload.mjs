import { parseArgs } from "node:util";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { createHash } from "node:crypto";

const { values } = parseArgs({ options: {
  command: { type: "string" }, fixture: { type: "string" }, output: { type: "string" },
  url: { type: "string" }, login: { type: "string" }, password: { type: "string" },
  participants: { type: "string", default: "16" }, seconds: { type: "string", default: "30" },
  cooldown: { type: "string", default: "65" },
  "runtime-image": { type: "string" },
} });
const fixturePath = resolve(values.fixture ?? "");
if (!values.fixture || !["seed", "extend", "run", "runtime", "verify"].includes(values.command))
  throw new Error("Use --command seed|extend|run|runtime|verify --fixture <private JSON path>.");

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const expect = (condition, message) => { if (!condition) throw new Error(message); };
const samples = [];
// These API types currently serialize domain enums as numbers, unlike CTF protocol enums.
const theoryProtocol = { singleChoice: 0, basic: 0, tag: 1, available: 1, practice: 0, highest: 0, publish: 0 };
let phase = "setup";
let baseUrl;

function localTarget(value) {
  const url = new URL(value);
  expect(["127.0.0.1", "localhost", "[::1]", "host.docker.internal"].includes(url.hostname),
    "This synthetic workload is restricted to the local acceptance host.");
  expect(url.protocol === "http:" || url.protocol === "https:", "HTTP or HTTPS is required.");
  return url.origin;
}

async function request(method, path, body, token, expected = [200], kind = "setup") {
  const started = performance.now();
  let status = 0;
  let bytes = 0;
  let ok = false;
  try {
    const response = await fetch(baseUrl + "/api/v1" + path, {
      method, headers: { ...(token ? { Authorization: "Bearer " + token } : {}),
        ...(["POST", "PUT", "PATCH", "DELETE"].includes(method) ? { "Idempotency-Key": crypto.randomUUID() } : {}),
        ...(body === undefined || body instanceof FormData ? {} : { "Content-Type": "application/json" }) },
      body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body), signal: AbortSignal.timeout(20_000),
    });
    status = response.status;
    const text = await response.text();
    bytes = new TextEncoder().encode(text).length;
    const value = text ? JSON.parse(text) : null;
    ok = expected.includes(status);
    if (!ok) throw new Error(`${kind}: HTTP ${status}; code=${value?.code ?? value?.title ?? "unspecified"}`);
    return value;
  } finally {
    if (kind !== "setup") samples.push({ kind, phase, milliseconds: performance.now() - started, status, ok, bytes });
  }
}

async function login(account, measured = false) {
  const result = await request("POST", "/auth/login", { login: account.login, password: account.password },
    undefined, [200], measured ? "login" : "setup");
  expect(result.state === "Authenticated", "Synthetic account did not authenticate without MFA.");
  account.token = result.accessToken;
  return result;
}

async function seed() {
  expect(values.url && values.login && values.password, "Seed requires local URL and synthetic administrator credentials.");
  baseUrl = localTarget(values.url);
  const participantCount = Number(values.participants);
  expect(Number.isInteger(participantCount) && participantCount >= 2 && participantCount <= 32,
    "Use 2 to 32 synthetic participants per fixture.");
  const runId = crypto.randomUUID().replaceAll("-", "").slice(0, 12);
  const admin = { login: values.login, password: values.password };
  await login(admin);
  const competition = await request("POST", "/admin/competitions", {
    title: "Synthetic runtime acceptance " + runId, description: "Local synthetic GC acceptance only.",
    mode: "Ctf", startTime: new Date(Date.now() - 60_000).toISOString(),
    endTime: new Date(Date.now() + 3 * 86_400_000).toISOString(), maxTeamMembers: 1,
    maxConcurrentRuntimeInstancesPerTeam: 1, allowTeamRegistrationWhileRunning: true,
  }, admin.token, [201]);
  const template = await request("POST", "/admin/challenges", {
    title: "Synthetic flag task " + runId, description: "A synthetic workload, with no real challenge secret.",
    direction: "General", mode: "Ctf", visibility: "Private",
    definition: { mode: "Ctf", ctf: { interactionKind: "FlagSubmission" } },
  }, admin.token, [201]);
  const correctFlag = "flag{synthetic-" + runId + "}";
  await request("POST", `/admin/challenges/${template.id}/flags`, { flag: correctFlag, matchKind: "Exact" }, admin.token, [201]);
  const challenge = await request("POST", `/admin/competitions/${competition.id}/challenges`,
    { challengeId: template.id, order: 0 }, admin.token, [201]);
  await request("PATCH", `/admin/competitions/${competition.id}/challenges/${challenge.id}`, {
    presentation: { customTitle: null, order: 0, isPublished: true },
  }, admin.token, [200, 204]);
  for (const status of ["Visible", "Published"])
    await request("PUT", `/admin/competitions/${competition.id}/status`, { status }, admin.token, [204]);
  const participants = [];
  for (let index = 0; index < participantCount; index++) {
    const account = { login: `gc_${runId}_${index}`, password: "Synthetic-" + crypto.randomUUID() };
    await request("POST", "/auth/register", {
      userName: account.login, email: account.login + "@example.invalid", password: account.password,
    }, undefined, [201]);
    const authenticated = await login(account);
    const team = await request("POST", `/competitions/${competition.id}/teams`, {
      name: `Synthetic ${runId} ${index}`,
    }, account.token, [201]);
    await request("PATCH", `/competitions/${competition.id}/teams/${team.id}`,
      { registration: { status: "Pending" } }, account.token, [200, 204]);
    participants.push({ ...account, userId: authenticated.userId, teamId: team.id });
  }
  const gate = await request("GET", `/admin/competitions/${competition.id}/start-validation`, undefined, admin.token);
  expect(gate.errors.length === 0, "Synthetic start gate failed: " + gate.errors.map(error => error.code).join(","));
  await request("PUT", `/admin/competitions/${competition.id}/status`, { status: "Running" }, admin.token, [204]);
  const fixture = { format: 1, synthetic: true, createdAt: new Date().toISOString(), baseUrl, runId,
    competitionId: competition.id, templateId: template.id, challengeId: challenge.id, correctFlag, admin, participants };
  await extendFixture(fixture);
  fixture.setupMeasurements = samples.slice();
  await mkdir(dirname(fixturePath), { recursive: true });
  await writeFile(fixturePath, JSON.stringify(fixture, null, 2), { flag: "wx", mode: 0o600 });
  console.log(JSON.stringify({ status: "seeded", competitionId: fixture.competitionId, participants: participants.length }));
}

async function extendFixture(fixture) {
  if (!fixture.attachment) {
    // Deterministic synthetic bytes: identical size and hash across independent fixtures.
    const payload = Buffer.alloc(8 * 1024 * 1024);
    for (let index = 0; index < payload.length; index++) payload[index] = index % 251;
    const form = new FormData();
    form.set("DeliveryPolicy", "All");
    form.append("Files", new Blob([payload], { type: "application/octet-stream" }), "synthetic-runtime.bin");
    const uploaded = await request("POST", `/admin/challenges/${fixture.templateId}/attachments`, form, fixture.admin.token, [201], "file-upload");
    const hash = createHash("sha256").update(payload).digest("hex");
    const attachment = uploaded.items.find(item => item.fileName === "synthetic-runtime.bin");
    expect(attachment?.sha256.toLowerCase() === hash && attachment.byteLength === payload.length, "Upload metadata integrity mismatch.");
    fixture.attachment = { id: attachment.id, sha256: hash, bytes: payload.length };
  }
  if (!fixture.theoryActivityId) {
    for (let index = 0; index < 8; index++) {
    const question = await request("POST", "/theory/questions", { content: {
      title: "Synthetic runtime question", body: "Select the synthetic correct option.", explanation: "Synthetic answer.",
      kind: theoryProtocol.singleChoice, difficulty: theoryProtocol.basic, options: [
        { content: "synthetic-correct", isCorrect: true }, { content: "synthetic-wrong", isCorrect: false }],
      classifications: [{ kind: theoryProtocol.tag, name: fixture.runId }], fileIds: [],
    } }, fixture.admin.token);
    await request("PUT", `/theory/questions/${question.questionId}/state`, { state: theoryProtocol.available }, fixture.admin.token, [204]);
    }
    await createTheoryActivity(fixture);
  }
}

async function createTheoryActivity(fixture) {
  const activity = await request("POST", "/theory/activities", { input: {
    name: "Synthetic runtime theory " + fixture.runId, kind: theoryProtocol.practice,
    opensAt: new Date(Date.now() - 60_000).toISOString(), closesAt: new Date(Date.now() + 3 * 86_400_000).toISOString(),
    durationSeconds: 600, attemptLimit: null, scoringMode: theoryProtocol.highest, allowExposedQuestions: true,
    rules: [{ kind: theoryProtocol.singleChoice, difficulty: theoryProtocol.basic, count: 1, points: 10, filters: [{ kind: theoryProtocol.tag, name: fixture.runId }] }],
  } }, fixture.admin.token);
  await request("POST", `/theory/activities/${activity.id}/operations`, { action: theoryProtocol.publish }, fixture.admin.token);
  fixture.theoryActivityId = activity.id;
}

async function transfer(fixture, account, cancel) {
  const started = performance.now();
  let bytes = 0, ok = false, status = 0;
  try {
    const response = await fetch(`${baseUrl}/api/v1/competitions/${fixture.competitionId}/challenges/${fixture.challengeId}/attachments/${fixture.attachment.id}`,
      { headers: { Authorization: "Bearer " + account.token }, signal: AbortSignal.timeout(30_000) });
    status = response.status;
    expect(status === 200, `File download returned HTTP ${status}.`);
    const hash = createHash("sha256");
    const reader = response.body.getReader();
    while (true) {
      const chunk = await reader.read();
      if (chunk.done) break;
      bytes += chunk.value.length;
      hash.update(chunk.value);
      if (cancel) { await reader.cancel(); break; }
    }
    if (cancel) expect(bytes > 0 && bytes < fixture.attachment.bytes, "Download cancellation did not interrupt the stream.");
    else expect(bytes === fixture.attachment.bytes && hash.digest("hex") === fixture.attachment.sha256, "Downloaded content integrity mismatch.");
    ok = true;
  } finally {
    samples.push({ kind: cancel ? "file-download-cancel" : "file-download-sha256", phase,
      milliseconds: performance.now() - started, status, ok, bytes });
  }
}

async function theory(fixture, account) {
  const path = `/theory/activities/${fixture.theoryActivityId}`;
  const session = await request("GET", path + "/session", undefined, account.token, [200], "theory-session");
  let view = await request("POST", path + "/papers", { requestId: crypto.randomUUID(), expectedIssuedCount: session.issuedCount }, account.token, [200], "theory-issue");
  const paperPath = path + "/papers/" + view.paper.id;
  for (const item of view.paper.questions) {
    expect(!Object.hasOwn(item.options[0], "isCorrect"), "Theory response exposed answer metadata.");
    view = await request("PUT", paperPath + "/answers/" + item.id, { expectedRevision: item.answerRevision,
      requestId: crypto.randomUUID(), optionIds: [item.options.find(option => option.content === "synthetic-correct").id] }, account.token, [200], "theory-answer");
  }
  view = await request("POST", paperPath + "/submit", { expectedRevision: view.paper.answerRevision }, account.token, [200], "theory-submit");
  expect(view.paper.score === 10, "Theory scoring mismatch.");
}

async function leaderboard(fixture, account) {
  const deadline = performance.now() + 20_000;
  while (performance.now() < deadline) {
    const result = await request("GET", `/competitions/${fixture.competitionId}/leaderboard`, undefined, account.token, [200, 202], "leaderboard");
    if (result.state !== "Processing") return result;
    await sleep(2000);
  }
  throw new Error("Leaderboard did not finish projecting within 20 seconds.");
}

async function runtimeCycle() {
  expect(values.output && values["runtime-image"], "Runtime cycle requires --output and --runtime-image.");
  const fixture = JSON.parse(await readFile(fixturePath, "utf8"));
  expect(fixture.synthetic === true, "Runtime cycle requires synthetic input.");
  baseUrl = localTarget(fixture.baseUrl);
  await login(fixture.admin);
  phase = "runtime";
  const runtimes = [];
  let failure;
  try {
    if (!fixture.runtimeTemplateId) {
      const template = await request("POST", "/admin/challenges", {
        title: "Synthetic Runtime " + fixture.runId, description: "Isolated GC acceptance workload.",
        mode: "Ctf", direction: "General", visibility: "Private",
        definition: { mode: "Ctf", ctf: { interactionKind: "FlagSubmission" }, runtime: {
          kind: "Container", allocation: "PerTeam", flagSource: "Static", egressPolicy: "Isolated",
          ttlSeconds: 600, operationTimeoutSeconds: 120,
          urlBindings: [{ urlTemplate: "http://{HOST}:{PORT}", exposure: "OwnerOnly", containerPort: 80, serviceName: "web", isControlCheck: false }],
          container: { services: [{ name: "web", image: values["runtime-image"], cpuCores: .1, memoryMiB: 64, internalPorts: [80] }] },
        } },
      }, fixture.admin.token, [201], "runtime-template");
      fixture.runtimeTemplateId = template.id;
      await request("POST", `/admin/challenges/${template.id}/flags`, { flag: "flag{synthetic-runtime-only}", matchKind: "Exact" }, fixture.admin.token, [201]);
      await writeFile(fixturePath, JSON.stringify(fixture, null, 2), { mode: 0o600 });
    }
    const path = `/admin/challenges/${fixture.runtimeTemplateId}/test-runtimes`;
    async function waitFor(id, state) {
      const deadline = performance.now() + 150_000;
      while (performance.now() < deadline) {
        const result = await request("GET", path + "/current", undefined, fixture.admin.token, [200, 404], "runtime-status");
        if (state === "Stopped" && result === null) return;
        expect(result !== null && result.id === id, "Runtime identity changed unexpectedly.");
        if (result.state === state) return;
        expect(result.state !== "Failed", "Runtime failed: " + result.failureCode);
        await sleep(1000);
      }
      throw new Error("Runtime did not reach " + state + " within 150 seconds.");
    }
    let current;
    const existing = await request("GET", path + "/current", undefined, fixture.admin.token, [200, 404]);
    if (existing && !["Stopped", "Failed"].includes(existing.state)) {
      await request("DELETE", path + "/" + existing.id, undefined, fixture.admin.token, [202], "runtime-cleanup");
      await waitFor(existing.id, "Stopped");
    }
    for (const reset of [false, true]) {
      const started = performance.now();
      const accepted = await request("POST", path, reset ? { replacesRuntimeId: current } : {}, fixture.admin.token, [202], reset ? "runtime-reset" : "runtime-create");
      expect(!reset || accepted.runtimeInstanceId !== current, "Reset reused a Runtime UUID.");
      current = accepted.runtimeInstanceId;
      runtimes.push(current);
      await waitFor(current, "Running");
      samples.push({ kind: "runtime-running", phase, milliseconds: performance.now() - started, status: 200, ok: true, bytes: 0 });
    }
    const started = performance.now();
    await request("DELETE", path + "/" + current, undefined, fixture.admin.token, [202], "runtime-stop");
    await waitFor(current, "Stopped");
    samples.push({ kind: "runtime-stopped", phase, milliseconds: performance.now() - started, status: 200, ok: true, bytes: 0 });
  } catch (error) { failure = error.message; process.exitCode = 1; }
  const report = { synthetic: true, kind: "runtime-cycle", finishedAt: new Date().toISOString(), runtimes,
    samples, status: failure ? "failed" : "passed", failure };
  await writeFile(values.output, JSON.stringify(report, null, 2), { mode: 0o600 });
  console.log(JSON.stringify({ status: report.status, runtimes: runtimes.length, failure }));
}

class CompetitionConnection {
  constructor(account, competitionId) {
    this.account = account;
    this.competitionId = competitionId;
    this.events = 0;
    this.completions = new Map();
    this.sequence = 0;
  }
  async connect() {
    const endpoint = new URL(baseUrl + "/hubs/v1/competitions");
    endpoint.protocol = endpoint.protocol === "https:" ? "wss:" : "ws:";
    endpoint.searchParams.set("access_token", this.account.token);
    this.socket = new WebSocket(endpoint);
    const started = performance.now();
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error("SignalR handshake timed out.")), 20_000);
      this.socket.onopen = () => this.socket.send('{"protocol":"json","version":1}\u001e');
      this.socket.onerror = () => { clearTimeout(timeout); reject(new Error("SignalR connection failed.")); };
      this.socket.onmessage = event => {
        for (const frame of String(event.data).split("\u001e").filter(Boolean)) {
          const message = JSON.parse(frame);
          if (message.type === undefined) {
            clearTimeout(timeout);
            message.error ? reject(new Error("SignalR handshake rejected.")) : resolve();
          } else if (message.type === 1) this.events++;
          else if (message.type === 3) this.completions.get(message.invocationId)?.(message);
        }
      };
    });
    await this.invoke("JoinCompetition", [this.competitionId]);
    this.keepAlive = setInterval(() => {
      if (this.socket.readyState === WebSocket.OPEN) this.socket.send('{"type":6}\u001e');
    }, 5_000);
    samples.push({ kind: "signalr-connect", phase, milliseconds: performance.now() - started, status: 101, ok: true, bytes: 0 });
  }
  async invoke(target, args) {
    const invocationId = String(++this.sequence);
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => { this.completions.delete(invocationId); reject(new Error("SignalR invocation timed out.")); }, 20_000);
      this.completions.set(invocationId, message => {
        clearTimeout(timeout); this.completions.delete(invocationId);
        message.error ? reject(new Error("SignalR invocation rejected.")) : resolve();
      });
      this.socket.send(JSON.stringify({ type: 1, invocationId, target, arguments: args }) + "\u001e");
    });
  }
  close() { clearInterval(this.keepAlive); this.socket?.close(1000); }
}

async function submit(fixture, account, flag, expectedResult) {
  const started = performance.now();
  let ok = false;
  try {
    let result = await request("POST", `/competitions/${fixture.competitionId}/challenges/${fixture.challengeId}/flag-submissions`,
      { flag }, account.token, [200, 202], "flag-intake");
    const factId = result.gameplayFactId;
    expect(typeof factId === "string", "Flag intake did not return a gameplay fact identity.");
    const deadline = performance.now() + 20_000;
    while (result.state !== "Completed" && performance.now() < deadline) {
      expect(result.state !== "PlatformFailed", "Worker returned a platform failure.");
      await sleep(50);
      result = await request("GET", `/competitions/${fixture.competitionId}/gameplay-facts/${factId}`,
        undefined, account.token, [200], "fact-status");
    }
    expect(result.state === "Completed" && result.result === expectedResult,
      `Worker result mismatch: state=${result.state}, result=${result.result}, failure=${result.failureCode ?? "none"}.`);
    ok = true;
  } finally {
    samples.push({ kind: "worker-completion", phase, milliseconds: performance.now() - started, status: ok ? 200 : 0, ok, bytes: 0 });
  }
}

function summary(items) {
  const sorted = items.map(item => item.milliseconds).sort((a, b) => a - b);
  const percentile = fraction => sorted[Math.max(0, Math.ceil(sorted.length * fraction) - 1)] ?? null;
  return { count: items.length, successful: items.filter(item => item.ok).length,
    errors: items.filter(item => !item.ok).length, p50Ms: percentile(.5), p95Ms: percentile(.95), p99Ms: percentile(.99),
    bytes: items.reduce((total, item) => total + item.bytes, 0),
    statusCounts: Object.fromEntries([...new Set(items.map(item => item.status))].map(status => [status, items.filter(item => item.status === status).length])) };
}

async function verifyScore(fixture) {
  const account = fixture.participants[0];
  if (!fixture.scoreProof) {
    // Formal static CTF requires a completed acquisition of its attachment.
    await transfer(fixture, account, false);
    await submit(fixture, account, fixture.correctFlag, "Correct");
  }
  const deadline = performance.now() + 20_000;
  while (performance.now() < deadline) {
    const snapshot = await leaderboard(fixture, account);
    const team = snapshot.teams.find(item => item.teamId === account.teamId);
    if (team?.totalScore > 0) {
      if (fixture.scoreProof) expect(team.totalScore === fixture.scoreProof.totalScore,
        "Persisted score changed without another successful action.");
      else {
        fixture.scoreProof = { teamId: account.teamId, totalScore: team.totalScore };
        await writeFile(fixturePath, JSON.stringify(fixture, null, 2), { mode: 0o600 });
      }
      return;
    }
    await sleep(500);
  }
  throw new Error("Correct Flag did not reach the projected leaderboard.");
}

async function run() {
  expect(values.output, "Run requires --output <JSON report path>.");
  const fixture = JSON.parse(await readFile(fixturePath, "utf8"));
  expect(fixture.synthetic === true && fixture.format === 1, "A synthetic fixture is required.");
  baseUrl = localTarget(values.url ?? fixture.baseUrl);
  const seconds = Number(values.seconds);
  const cooldown = Number(values.cooldown);
  expect(Number.isFinite(cooldown) && cooldown >= 65 && cooldown <= 600, "Recovery cooldown must be 65 to 600 seconds.");
  expect(fixture.attachment && fixture.theoryActivityId, "Extend this fixture before running the complete workload.");
  expect(Number.isFinite(seconds) && seconds >= 5 && seconds <= 600, "Phase duration must be 5 to 600 seconds.");
  const connections = [];
  const errors = [];
  const phases = [];
  const startedAt = new Date().toISOString();
  try {
    await login(fixture.admin);
    // Each participant draws a source question only once per activity. Renew the
    // practice activity between runs; allowExposedQuestions does not reset draws.
    if (fixture.theoryWorkloadStarted) {
      await createTheoryActivity(fixture);
    }
    fixture.theoryWorkloadStarted = true;
    await writeFile(fixturePath, JSON.stringify(fixture, null, 2), { mode: 0o600 });
    for (const account of fixture.participants) await login(account);
    await verifyScore(fixture);
    for (const account of fixture.participants.slice(0, 8)) {
      const connection = new CompetitionConnection(account, fixture.competitionId);
      connections.push(connection);
      await connection.connect();
    }
    for (const settings of [
      { name: "warmup", concurrency: 2 }, { name: "steady", concurrency: 4 },
      { name: "burst", concurrency: 16 }, { name: "overload", concurrency: 32 }, { name: "recovery", concurrency: 4 },
    ]) {
      phase = settings.name;
      if (phase === "recovery") await sleep(cooldown * 1000);
      const started = performance.now();
      const phaseStartedAt = new Date().toISOString();
      const before = samples.length;
      const deadline = started + seconds * 1000;
      await Promise.all(Array.from({ length: settings.concurrency }, async (_, worker) => {
        let sequence = worker;
        while (performance.now() < deadline) {
          const requestStarted = performance.now();
          // Reserve one solved team to verify durable score invariants across restarts.
          const account = fixture.participants[1 + worker % (fixture.participants.length - 1)];
          try {
            switch (sequence++ % 10) {
              case 0: await login(account, true); break;
              case 1: await submit(fixture, account, "incorrect-synthetic-" + crypto.randomUUID(), "Wrong"); break;
              case 2: case 3:
                await leaderboard(fixture, account); break;
              case 4: await transfer(fixture, account, worker % 2 === 0); break;
              default:
                await request("GET", `/competitions/${fixture.competitionId}/challenges/${fixture.challengeId}`, undefined, account.token, [200], "challenge-read"); break;
            }
          } catch (error) {
            if (errors.length < 100) errors.push({ phase, message: String(error.message).slice(0, 240) });
          }
          // Fixed pacing makes offered rates comparable; quota rejection stays visible.
          await sleep(Math.max(0, 500 - (performance.now() - requestStarted)));
        }
      }));
      const theoryResults = await Promise.allSettled(fixture.participants.slice(0, settings.concurrency).map(account => theory(fixture, account)));
      for (const result of theoryResults) if (result.status === "rejected")
        errors.push({ phase, message: String(result.reason.message).slice(0, 240) });
      connections[0].close();
      await connections[0].connect();
      const elapsedSeconds = (performance.now() - started) / 1000;
      const phaseSamples = samples.slice(before);
      phases.push({ phase, startedAt: phaseStartedAt, finishedAt: new Date().toISOString(), concurrency: settings.concurrency, elapsedSeconds,
        requests: summary(phaseSamples), effectiveCompletionsPerSecond: phaseSamples.filter(item => item.ok && item.kind !== "worker-completion").length / elapsedSeconds,
        completedWorkerFactsPerSecond: phaseSamples.filter(item => item.ok && item.kind === "worker-completion").length / elapsedSeconds,
        completedTheoryPapersPerSecond: phaseSamples.filter(item => item.ok && item.kind === "theory-submit").length / elapsedSeconds,
        scenarios: Object.fromEntries([...new Set(phaseSamples.map(item => item.kind))].map(kind => [kind, summary(phaseSamples.filter(item => item.kind === kind))])) });
    }
    await verifyScore(fixture);
  } catch (error) {
    errors.push({ phase, message: String(error.message).slice(0, 240) });
  } finally {
    for (const connection of connections) connection.close();
    const report = { format: 1, kind: "application-workload", synthetic: true, startedAt, finishedAt: new Date().toISOString(),
      competitionId: fixture.competitionId, theoryActivityId: fixture.theoryActivityId, scoreProof: fixture.scoreProof, phases, signalrEvents: connections.reduce((total, connection) => total + connection.events, 0), errors,
      driver: { node: process.version, phaseSeconds: seconds, cooldownSeconds: cooldown, participants: fixture.participants.length },
      scope: ["password-login", "challenge-read", "flag-intake", "worker-completion", "leaderboard-read", "signalr-connect-reconnect", "file-download-sha256", "file-download-cancel", "theory-submit"],
      status: errors.length === 0 && phases.length === 5 ? "passed" : "failed" };
    await mkdir(dirname(resolve(values.output)), { recursive: true });
    await writeFile(values.output, JSON.stringify(report, null, 2), { mode: 0o600 });
    console.log(JSON.stringify({ status: report.status, phases: phases.length, recordedErrors: errors.length }));
    if (report.status !== "passed") process.exitCode = 1;
  }
}

if (values.command === "verify") {
  expect(values.output, "Verify requires an output report.");
  const fixture = JSON.parse(await readFile(fixturePath, "utf8"));
  expect(fixture.synthetic === true && fixture.scoreProof, "A scored synthetic fixture is required.");
  baseUrl = localTarget(fixture.baseUrl);
  phase = "verification";
  let failure;
  try {
    await login(fixture.participants[0]);
    await verifyScore(fixture);
  } catch (error) { failure = error.message; process.exitCode = 1; }
  await writeFile(values.output, JSON.stringify({ status: failure ? "failed" : "passed", failure,
    synthetic: true, finishedAt: new Date().toISOString(), scoreProof: fixture.scoreProof, samples }, null, 2), { mode: 0o600 });
} else if (values.command === "extend") {
  const fixture = JSON.parse(await readFile(fixturePath, "utf8"));
  expect(fixture.synthetic === true, "Only synthetic fixtures may be extended.");
  baseUrl = localTarget(fixture.baseUrl);
  await login(fixture.admin);
  await extendFixture(fixture);
  await writeFile(fixturePath, JSON.stringify(fixture, null, 2), { mode: 0o600 });
  console.log("Synthetic fixture extended.");
} else await (values.command === "seed" ? seed() : values.command === "runtime" ? runtimeCycle() : run());
