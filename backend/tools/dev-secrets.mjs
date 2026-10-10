// Puts this machine's development secrets where the API reads them in Development: dotnet user-secrets, a file in
// your user profile, never in the repository (library 10.5 "Secrets").
//
//   node tools/dev-secrets.mjs            (from backend/; run once per machine, again to rotate the webhook secret)
//
// It sets:
//   Payments:Fake:WebhookSecret   a new random secret for the fake payment provider's webhook signatures
//   ConnectionStrings:Postgres    the docker-compose PostgreSQL (its password is the local container's, in docker-compose.yml)
// Where they live: %APPDATA%\Microsoft\UserSecrets\booking-app-api-7b3e2c1a\secrets.json (Windows),
//                  ~/.microsoft/usersecrets/booking-app-api-7b3e2c1a/secrets.json (macOS, Linux).
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';

const set = (key, value) =>
  execFileSync('dotnet', ['user-secrets', 'set', key, value, '--project', 'src/BookingApp.Api'], { stdio: ['ignore', 'ignore', 'inherit'] });

set('Payments:Fake:WebhookSecret', `whsec_${randomBytes(24).toString('hex')}`);
set('ConnectionStrings:Postgres', 'Host=localhost;Port=5432;Database=booking;Username=booking;Password=booking-dev-only;Maximum Pool Size=40');
console.log('Development secrets set. See them with: dotnet user-secrets list --project src/BookingApp.Api');
