// The booking race under load: many customers try to book the same slots at the same time. Every slot must end up
// with exactly one booking, and every other attempt must be told slot_taken. Library 12.4 "Load testing": real
// customers (each virtual user has its own cookie, so the API sees different people), real contention.
//
//   docker run --rm -i -e BASE=http://host.docker.internal:5080 -v "<backend>/perf:/scripts" grafana/k6:1.8.1 run /scripts/booking-race.js
//
// (Or with k6 installed: k6 run perf/booking-race.js, BASE defaults to http://localhost:5080.) Run it against the API
// on PostgreSQL (launch profile http-postgres) or SQLite, with the rate limits raised: every virtual user comes from
// the same IP (RateLimits__WritesPerMinutePerIp=100000 RateLimits__RequestsPerMinute=100000). docs/performance.md has
// the last results.
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE = __ENV.BASE || 'http://localhost:5080';
const SLOTS = Number(__ENV.SLOTS || 15);        // slots everyone fights over
const CUSTOMERS = Number(__ENV.CUSTOMERS || 100);

const booked = new Counter('booked');           // 201: this customer got the slot
const taken = new Counter('slot_taken');        // 409 slot_taken: someone else did
const limited = new Counter('limit_reached');   // 422 too_many_bookings: this customer already holds 5
const unexpected = new Counter('unexpected');   // anything else is a bug

export const options = {
  scenarios: {
    race: { executor: 'per-vu-iterations', vus: CUSTOMERS, iterations: 1, maxDuration: '2m' },
  },
  thresholds: {
    booked: [`count==${SLOTS}`],   // exactly one success per slot
    unexpected: ['count==0'],
    http_req_failed: ['rate<1'],   // 409s count as "failed" here; the counters above are the real check
  },
};

/** Free slots a week ahead, across the courts. */
export function setup() {
  const day = new Date(Date.now() + 7 * 864e5).toISOString().slice(0, 10);
  const slots = [];
  for (const resourceId of ['court-1', 'court-2', 'room-a']) {
    const availability = http.get(`${BASE}/api/resources/${resourceId}/availability?date=${day}`).json();
    for (const s of availability.slots.filter((x) => x.state === 'free')) {
      if (slots.length < SLOTS) slots.push({ resourceId, start: s.start, key: `${resourceId} ${s.localStart}` });
    }
  }
  if (slots.length < SLOTS) throw new Error(`only ${slots.length} free slots on ${day}`);
  console.log(`racing for ${slots.length} slots on ${day}: ${slots.map((s) => s.key).join(', ')}`);
  return { slots };
}

export default function (data) {
  // A customer cookie and an antiforgery token first, as the web app gets them; writes echo the token in a header.
  http.get(`${BASE}/api/bookings/mine`);
  http.get(`${BASE}/api/antiforgery`);
  const token = http.cookieJar().cookiesForURL(BASE)['XSRF-TOKEN'][0];
  const order = data.slots.map((s) => [Math.random(), s]).sort((a, b) => a[0] - b[0]).map(([, s]) => s);
  for (const slot of order) {
    const response = http.post(`${BASE}/api/bookings`,
      JSON.stringify({ resourceId: slot.resourceId, start: slot.start, slots: 1, name: `Racer ${__VU}` }),
      { headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': token }, tags: { slot: slot.key } });
    const code = response.status === 201 ? null : response.json('code');
    if (response.status === 201) booked.add(1, { slot: slot.key });
    else if (response.status === 409 && code === 'slot_taken') taken.add(1);
    else if (response.status === 422 && code === 'too_many_bookings') limited.add(1);
    else unexpected.add(1, { status: String(response.status), code: String(code) });
    check(response, { 'booked or a clear refusal': (r) => [201, 409, 422].includes(r.status) });
  }
}
