// Load test for the PlateBack API. Runs each scenario for a fixed time with
// a fixed number of connections and prints throughput and latency.
//
//   cd perf && npm ci && node load-test.mjs [label]
//
// Needs the API running (Release build) on http://localhost:5080 with the demo
// data plus perf/load-data.sql, and a high auth rate limit so the setup
// logins are not refused (RateLimits__AuthPerMinute=100000).
import autocannon from "autocannon";
import { writeFileSync, mkdirSync } from "node:fs";

const BASE = process.env.API_URL ?? "http://localhost:5080";
const DURATION = Number(process.env.DURATION ?? 15);
const CONNECTIONS = Number(process.env.CONNECTIONS ?? 20);
const WARMUP = Number(process.env.WARMUP ?? 3);
const label = process.argv[2] ?? "run";

async function login(email) {
  const response = await fetch(`${BASE}/api/v1/auth/login`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password: "password123" }),
  });
  if (!response.ok) throw new Error(`login ${email}: ${response.status}`);
  return (await response.json()).accessToken;
}

const maya = await login("maya@example.com");
const arjun = await login("arjun@sunrisebakery.example");
const arjunMe = await (await fetch(`${BASE}/api/v1/users/me`, { headers: { Authorization: `Bearer ${arjun}` } })).json();
const orgId = arjunMe.memberships[0].organizationId;
const someListing = (await (await fetch(`${BASE}/api/v1/listings?q=Croissant`)).json()).items[0].id;
const thamel = "lat=27.7154&lng=85.3123";

const scenarios = [
  { name: "Search 5 km, by distance (browse default)", path: `/api/v1/listings?${thamel}&radiusKm=5` },
  { name: "Search 25 km, by distance (largest radius)", path: `/api/v1/listings?${thamel}&radiusKm=25` },
  { name: "Search 5 km, page 10", path: `/api/v1/listings?${thamel}&radiusKm=5&page=10` },
  { name: "Search without location, by pickup time", path: "/api/v1/listings" },
  { name: "Search text \"momo\" within 5 km", path: `/api/v1/listings?${thamel}&radiusKm=5&q=momo` },
  { name: "Listing detail", path: `/api/v1/listings/${someListing}` },
  { name: "Impact stats (cached)", path: "/api/v1/stats/impact" },
  { name: "My reservations (signed in)", path: "/api/v1/reservations/mine?status=active", token: maya },
  { name: "Business dashboard stats", path: `/api/v1/organizations/${orgId}/stats`, token: arjun },
  { name: "Business listings table", path: `/api/v1/organizations/${orgId}/listings?pageSize=50`, token: arjun },
];

const only = process.env.ONLY;
const rows = [];
for (const s of scenarios.filter((x) => !only || x.name.includes(only))) {
  const options = {
    url: BASE + s.path,
    connections: CONNECTIONS,
    headers: s.token ? { Authorization: `Bearer ${s.token}` } : {},
  };
  // Unrecorded warm-up, so the first scenario does not pay for JIT compilation
  // and connection pool start-up.
  await autocannon({ ...options, duration: WARMUP });
  const result = await autocannon({ ...options, duration: DURATION });
  const errors = result.errors + result.non2xx;
  rows.push({
    scenario: s.name,
    rps: Math.round(result.requests.average),
    p50: result.latency.p50,
    p99: result.latency.p99,
    kb: Math.round(result.throughput.average / Math.max(result.requests.average, 1) / 1024 * 10) / 10,
    errors,
  });
  console.log(`${s.name.padEnd(46)} ${String(rows.at(-1).rps).padStart(6)} req/s  p50 ${String(result.latency.p50).padStart(4)} ms  p99 ${String(result.latency.p99).padStart(4)} ms  errors ${errors}`);
}

mkdirSync("results", { recursive: true });
const table = [
  `# Load test: ${label}`,
  "",
  `${new Date().toISOString()}, ${CONNECTIONS} connections, ${WARMUP} s warm-up then ${DURATION} s per scenario, API at ${BASE}`,
  "",
  "| Scenario | Requests/s | p50 (ms) | p99 (ms) | KB per response | Errors |",
  "| --- | ---: | ---: | ---: | ---: | ---: |",
  ...rows.map((r) => `| ${r.scenario} | ${r.rps} | ${r.p50} | ${r.p99} | ${r.kb} | ${r.errors} |`),
  "",
].join("\n");
writeFileSync(`results/${label}.md`, table);
console.log(`\nSaved results/${label}.md`);
