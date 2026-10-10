// Sends a signed webhook from the fake payment provider to the running API, as the provider would.
//
//   node tools/payment-webhook.mjs <type> <paymentId> [--event evt_123] [--reference fake_ch_...] [--bad-signature]
//   (run it from backend/)
//
//   type: payment.succeeded | payment.failed | refund.succeeded
//   paymentId: a booking's deposit.paymentId (GET /api/bookings/mine)
//   --event: the provider's event id. Send the same id twice to see the second delivery change nothing.
//   --bad-signature: sign with the wrong secret; the API answers 401 invalid_signature.
//
// Env: API (default http://localhost:5080), WEBHOOK_SECRET (default: the Development secret from dotnet user-secrets).
import { execFileSync } from 'node:child_process';
import { createHmac, randomUUID } from 'node:crypto';

const [type, paymentId, ...rest] = process.argv.slice(2);
if (!type || !paymentId) {
  console.error('usage: node tools/payment-webhook.mjs <type> <paymentId> [--event id] [--reference ref] [--bad-signature]');
  process.exit(2);
}
const option = (name) => { const i = rest.indexOf(name); return i >= 0 ? rest[i + 1] : undefined; };
const api = process.env.API ?? 'http://localhost:5080';
/** The Development secret the API reads (tools/dev-secrets.mjs put it in user-secrets). */
function userSecret(key) {
  const listed = execFileSync('dotnet', ['user-secrets', 'list', '--project', 'src/BookingApp.Api'], { encoding: 'utf8' });
  const line = listed.split(/?
/).find((l) => l.startsWith(`${key} = `));
  if (!line) throw new Error(`${key} is not in user-secrets: run node tools/dev-secrets.mjs first`);
  return line.slice(key.length + 3);
}
const secret = rest.includes('--bad-signature') ? 'not-the-shared-secret' : (process.env.WEBHOOK_SECRET ?? userSecret('Payments:Fake:WebhookSecret'));

const body = JSON.stringify({
  id: option('--event') ?? `evt_${randomUUID().replaceAll('-', '')}`,
  type,
  paymentId,
  // The fake provider names a charge fake_ch_<payment id without dashes>.
  providerReference: option('--reference') ?? `fake_ch_${paymentId.replaceAll('-', '')}`,
});
const t = Math.floor(Date.now() / 1000);
const signature = createHmac('sha256', secret).update(`${t}.${body}`).digest('hex');

const response = await fetch(`${api}/api/payments/webhook`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json', 'Payment-Signature': `t=${t},v1=${signature}` },
  body,
});
console.log(`${response.status} ${await response.text()}`);
