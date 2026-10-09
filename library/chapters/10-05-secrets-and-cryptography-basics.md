---
id: "10-5"
number: "10.5"
title: "Secrets and cryptography basics"
part: 10
part_title: "Security"
words: 7904
minutes: 34
---

# 10.5 Secrets and cryptography basics

> **Quick look.** The few cryptographic building blocks you need, and the rules that come with each.
>
> - Never design your own crypto; use standard building blocks.
> - Hashes (SHA-256) fingerprint data; HMAC adds a key to prove origin; compare secrets in constant time.
> - Passwords: Argon2id, or PBKDF2 with about 600,000 iterations; rehash as parameters rise.
> - Encrypt with authenticated encryption (AES-GCM) and never reuse a nonce with a key.
> - Use RandomNumberGenerator for secrets, not Random; Base64 is encoding, not encryption.
> - Keep keys in a vault, use envelope encryption, and support two keys during rotation.
> - Persist and share the Data Protection key ring, or every deploy logs users out.
> - No secrets in code, images or logs; prefer managed identities so there is no secret at all; rotate on any leak.


### The big picture

Cryptography is what makes the rest of security possible. It lets a browser trust a bank's server, from TLS (6.5). It lets an API trust a token it didn't issue, from OAuth 2 and OpenID Connect (10.3). And it means a stolen database of passwords is useless to the thief. Using it well needs no mathematics, only knowing which of a few standard building blocks solves which problem, and the handful of rules that each one comes with. Using it badly is easy, and failures are silent: encrypted data with a reused nonce, or passwords hashed with the wrong algorithm, look exactly like the real thing until someone breaks them.

This chapter covers the building blocks: hashes, message authentication codes, password hashing, authenticated encryption, signatures and secure randomness. For each, it says what problem it solves and the rule that keeps it safe. It then covers the part most breaches actually come from, which is keys and secrets. That means where connection strings, API keys and signing keys should live, how services can prove who they are without any stored secret, and what to do when a secret leaks. The example uses only .NET's built-in cryptography. The printed hashes and codes were computed independently with other implementations of the same standard algorithms, and the comments show the exact values you should see.

### Before you start

**Builds on:** 6.5 TLS, 10.3 OAuth 2 and OpenID Connect, 3.3 Hash tables, 8.2 API design, 8.1 What a backend does, 10.1 Thinking like an attacker.

**Ideas explained later:**

- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Git** (11.1): The distributed version control system used almost everywhere, created in 2005.
- **Version control** (11.1): Recording the history of files so past states can be recovered and changes combined and explained.
- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **AWS** (11.5): Amazon Web Services, the largest cloud provider, launched in 2006.
- **Logging** (11.6): Recording events as log lines, each with a time, level, message and fields.

**Tools and technologies named here:**

- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

**The code uses:** variables and var, string interpolation, LINQ, exceptions, using for cleanup. Each is explained in 0.1 Reading the code in this guide.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Why is SHA-256 the wrong way to store passwords, when it is a secure hash?**

> It is designed to be fast: a graphics card computes billions of SHA-256 hashes a second, so leaked hashes of ordinary passwords are guessed quickly. Password hashing needs a deliberately slow, salted algorithm, such as Argon2, scrypt, bcrypt or PBKDF2.

**2. What does a salt do?**

> It is a random value stored with each password hash and mixed into it, so identical passwords produce different hashes and precomputed tables of hashes are useless. An attacker must attack each hash separately.

**3. What is the difference between a hash and an HMAC?**

> Anyone can compute a hash, so it shows that data changed, but not who produced it. An HMAC also needs a secret key, so only key holders can create or check it. That proves data came from someone with the key and wasn't altered.

**4. What is the one rule you must never break with AES-GCM?**

> Never use the same nonce twice with the same key. Reusing a nonce lets an attacker recover information about the plaintexts and forge messages.

**5. Why compare MACs and tokens with a constant-time function?**

> An ordinary comparison stops at the first different byte, so response times reveal how many leading bytes were right. An attacker can use that to work out a valid value byte by byte. A constant-time comparison always takes the same time.

**6. Why is System.Random unsuitable for reset tokens?**

> It is a predictable pseudo-random generator: its future output can be worked out from its seed or earlier outputs. Security tokens, keys and salts need a cryptographically secure generator, `RandomNumberGenerator`.

**7. What is a managed identity?**

> An identity the cloud platform gives a service, such as an app in Azure, which it can use to get tokens for databases and key vaults without any password or key stored anywhere in the app.

### Don't invent, choose

The first rule of cryptography in applications is not to design any of it. Algorithms, and the ways they are combined, are designed by specialists and then attacked in public for years before anyone trusts them. Even small changes to a sound design can break it. An application developer's job is to pick the right standard building block, use a well-maintained library's high-level API, and follow the rules that come with it. .NET's `System.Security.Cryptography` namespace provides everything in this chapter.

### Hashes

A **cryptographic hash** turns any amount of data into a fixed-size fingerprint, 32 bytes for SHA-256, with properties that ordinary hash functions from Hash tables (3.3) don't have. It is one-way: you can't work back from the fingerprint to the data. It has **collision resistance**: nobody can find two different inputs with the same fingerprint. And any change to the input, even one character, changes the output completely, as the example shows.

Hashes check integrity: a published SHA-256 of a download lets you confirm the file wasn't altered or corrupted. Git names every file and commit by its hash, as Version control with Git (11.1) explains. SHA-256 and the SHA-3 family are current standards. MD5 and SHA-1 are broken, since researchers have produced collisions for both, and must not be used for security.

A hash alone proves nothing about who made it: an attacker who changes the data simply recomputes the hash. For that, you need a key.

### Message authentication codes

A **message authentication code** (MAC) is a short tag computed from data and a secret key. Only someone with the key can compute a correct tag, so a valid tag proves the data came from a key holder and wasn't changed. **HMAC**, a MAC built from a hash function, is the most common: HMAC-SHA256 signs webhook payloads, from API design (8.2), signed cookies and many API request schemes.

Checking a tag has a trap. An ordinary comparison stops at the first byte that differs, so it takes slightly longer the more leading bytes are right. Over many attempts, an attacker who can measure response times can work out a valid tag one byte at a time. This is a **timing attack**. The defence is a **constant-time comparison**, which always examines every byte: `CryptographicOperations.FixedTimeEquals` in .NET. Use it whenever you compare MACs, tokens or password hashes.

### Password hashing

Passwords need a different tool again, because the threat is different. When a database of password hashes leaks, the attacker doesn't need to reverse the hashes. They guess: hash likely passwords, from leaked-password lists, dictionaries and patterns, and compare. A fast hash helps them. A single modern graphics card computes billions of SHA-256 hashes per second, so most human-chosen passwords protected by plain SHA-256 fall within hours.

A **password hashing function** is designed to be slow and, in its better forms, to need a lot of memory, which graphics cards and custom chips can't provide cheaply. Its cost is adjustable through a **work factor**, such as an iteration count, so it can be raised as hardware gets faster. Argon2id is the current first choice, with scrypt and bcrypt close behind. PBKDF2 is the one to use where official standards compliance matters; OWASP recommends 600,000 iterations with HMAC-SHA256. .NET has PBKDF2 built in, and ASP.NET Core Identity's password hasher uses it.

Each password is hashed with its own **salt**: a random value, 16 bytes is typical, stored next to the hash. Without a salt, two users with the same password have the same hash, and an attacker can use huge precomputed tables of hashes for common passwords, called rainbow tables. With a unique salt per user, every hash must be attacked separately, from scratch.

Store the algorithm, work factor, salt and hash together, so parameters can change over time. When a user signs in successfully with a hash made under older parameters, rehash the password with the current ones.

### Encryption

Encryption keeps data confidential, as TLS (6.5) introduced. Symmetric encryption uses one shared key, and AES, the Advanced Encryption Standard, is the algorithm used almost everywhere, with keys of 128 or 256 bits.

How AES is used matters as much as AES itself. Plain encryption hides data but doesn't stop changes. An attacker can flip bits in the encrypted data, and the matching bits in the decrypted result flip too, without anyone noticing. **Authenticated encryption** solves both problems at once: it encrypts and also produces a tag, like a MAC, that decryption checks, refusing any ciphertext that was altered. AES-GCM is the standard choice, and ChaCha20-Poly1305 is the usual alternative. Use one of them. Avoid older modes, such as ECB, which encrypts identical blocks identically and so leaks patterns, and don't combine encryption and MACs yourself.

AES-GCM takes a **nonce**, a "number used once", with each encryption: 12 bytes, which must never repeat with the same key. Reusing a nonce with the same key is catastrophic, revealing information about both messages and allowing forgery. Generate nonces randomly, or from a counter that can never reset, and rotate keys long before a random nonce could plausibly repeat. It can also bind associated data: information that isn't encrypted but is covered by the tag, such as which booking a passport number belongs to, so an attacker can't move an encrypted value from one record to another.

### Signatures

Public-key cryptography, from TLS (6.5), uses a key pair: a private key kept secret and a public key shared with anyone. A digital signature is made with the private key and checked with the public one. Anyone can verify, but only the key's owner can sign. That is what lets APIs check JWTs from an identity provider, from OAuth 2 and OpenID Connect (10.3), and it signs certificates, software updates and code packages. ECDSA with the P-256 curve, Ed25519 and RSA with PSS padding are the common choices.

Public-key encryption is rarely used directly on data, because it is slow and limited in size. Instead, systems use hybrid encryption: public-key operations agree or protect a random symmetric key, which then encrypts the data with AES-GCM. TLS works this way.

Quantum computers, if large enough ones are ever built, would break today's public-key algorithms, though not AES with 256-bit keys. In 2024 NIST published the first standards for **post-quantum cryptography**, algorithms designed to resist such machines: ML-KEM for key agreement and ML-DSA for signatures. Browsers and TLS libraries already use hybrid post-quantum key agreement. The concern driving it is "harvest now, decrypt later": recording encrypted traffic today in the hope of decrypting it in future. For application code, the practical lesson is crypto agility: keep algorithms configurable and versioned, so they can be replaced without rewriting systems.

### Randomness

Keys, salts, nonces, reset tokens, session ids and API keys must be unpredictable. `System.Random`, like most languages' default random generators, is a pseudo-random generator designed for speed and statistics. Its output can be predicted from its seed or from earlier outputs, and with a fixed seed it produces the same sequence every run. For anything security-related, use a **cryptographically secure random number generator** (CSPRNG), which draws on the operating system's entropy: `RandomNumberGenerator` in .NET. It has convenient methods such as `GetBytes`, `GetInt32` and `GetHexString`. GUIDs are identifiers, not secrets; generate tokens with `RandomNumberGenerator` instead.

### Encoding is not encryption

Base64, hex and URL encoding change how bytes are written, not who can read them. A base64 string decodes in one step, with no key, as the JWT payload in OAuth 2 and OpenID Connect (10.3) did. Encoding is for transport. Hashing is for fingerprints and password storage. Encryption is for confidentiality, and it needs a key.

### Seeing it for real

This program uses each building block once. The hashes, MAC and password hash are fixed values, so they should match the comments exactly on any machine. Those values were computed independently with other implementations of the same algorithms. The encryption and signature parts use random keys and nonces, so they show behaviour rather than fixed values.

```csharp
// crypto.cs — run with: dotnet run crypto.cs
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

byte[] booking = Encoding.UTF8.GetBytes("BA117 LHR-JFK 2026-09-28 fare 450.00");
byte[] altered = Encoding.UTF8.GetBytes("BA117 LHR-JFK 2026-09-28 fare 450.01");

// 1. A cryptographic hash: a fixed-size fingerprint; one changed character changes all of it
Console.WriteLine(Convert.ToHexStringLower(SHA256.HashData(booking)));
// 9357b30c393955adbeb1ec31072e30ee9a00b10da8037230e4106a17d08107f2
Console.WriteLine(Convert.ToHexStringLower(SHA256.HashData(altered)));
// cb92dfaa1d9baa583b0449dcb4922acfc7dea603ba5de1a750f767d21cb64415

// 2. An HMAC: a hash that also needs a secret key, so only key holders can make or check it
byte[] macKey = Encoding.UTF8.GetBytes("demo key: in real code, 32 random bytes from a key vault");
byte[] tag = HMACSHA256.HashData(macKey, booking);
Console.WriteLine(Convert.ToHexStringLower(tag));
// 76b48a10ecca01fcb05ff8e09b422afe992b491c1d3ea6acc9de7c687dd226ac
Console.WriteLine(CryptographicOperations.FixedTimeEquals(tag, HMACSHA256.HashData(macKey, altered)));   // False: the fare changed

// 3. Password hashing: slow on purpose, and salted
byte[] salt = Convert.FromHexString("8f2a1c9e4b7d3f60a5e2c8b1d4f7093e");   // really RandomNumberGenerator.GetBytes(16), stored with the hash
var sw = Stopwatch.StartNew();
byte[] stored = Rfc2898DeriveBytes.Pbkdf2("correct horse battery staple", salt, 600_000, HashAlgorithmName.SHA256, 32);
Console.WriteLine($"{Convert.ToHexStringLower(stored)} in {sw.ElapsedMilliseconds} ms");
// 6b86a8b6b6c54719ff32e4cb4b92dbd7260b33d5d1928ae04960b2b89934bf60 in roughly 150 to 300 ms

// 4. Authenticated encryption with AES-GCM: confidential, and any change is detected
byte[] key = RandomNumberGenerator.GetBytes(32);                           // a 256-bit key: really from a key vault
byte[] passport = Encoding.UTF8.GetBytes("P1234567");
byte[] nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);   // 12 bytes, never reused with this key
byte[] ciphertext = new byte[passport.Length];
byte[] authTag = new byte[AesGcm.TagByteSizes.MaxSize];
byte[] context = "booking 1"u8.ToArray();                                  // associated data: bound to the ciphertext, not secret
using (var aes = new AesGcm(key, authTag.Length))
{
    aes.Encrypt(nonce, passport, ciphertext, authTag, context);
    byte[] decrypted = new byte[ciphertext.Length];
    aes.Decrypt(nonce, ciphertext, authTag, decrypted, context);
    Console.WriteLine(Encoding.UTF8.GetString(decrypted));                 // P1234567

    try { aes.Decrypt(nonce, ciphertext, authTag, decrypted, "booking 2"u8.ToArray()); }   // moved to another booking
    catch (AuthenticationTagMismatchException) { Console.WriteLine("rejected: wrong booking"); }
    ciphertext[0] ^= 1;                                                    // flip one bit, as an attacker might
    try { aes.Decrypt(nonce, ciphertext, authTag, decrypted, context); }
    catch (AuthenticationTagMismatchException) { Console.WriteLine("rejected: ciphertext was altered"); }
}

// 5. A digital signature: only the private key can sign; anyone with the public key can verify
using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
byte[] signature = signer.SignData(booking, HashAlgorithmName.SHA256);
using var verifier = ECDsa.Create(signer.ExportParameters(includePrivateParameters: false));   // what another system holds
Console.WriteLine($"{verifier.VerifyData(booking, signature, HashAlgorithmName.SHA256)} " +
                  $"{verifier.VerifyData(altered, signature, HashAlgorithmName.SHA256)}");       // True False

// 6. Randomness: unpredictable for secrets, predictable for everything else
Console.WriteLine(RandomNumberGenerator.GetHexString(32, lowercase: true));   // a reset token: different every run
Console.WriteLine(new Random(42).Next(1_000_000));                            // the same number every run: never for secrets
```

### Keys are the real secret

Every algorithm above is public. The only thing keeping data safe is the key, so key management is where cryptography succeeds or fails in practice.

Generate keys with a CSPRNG, never from a password or a memorable string. Use a separate key for each purpose and each environment, so a test key can't decrypt production data and a signing key never encrypts. Rotate keys on a schedule and after any suspected exposure, which means data and tokens must record which key version they used, as the `kid` in OAuth 2 and OpenID Connect (10.3) did.

Store keys where application code can use them without being able to copy them out. A **key vault**, such as Azure Key Vault, AWS KMS or Google Cloud KMS, holds keys in hardware security modules and performs operations on the application's behalf, logging every use. For bulk data, **envelope encryption** combines the two: each piece of data is encrypted locally with its own data key, and only that small data key is encrypted by a master key in the vault. The master key never leaves the vault, and rotating it doesn't mean re-encrypting all the data.

ASP.NET Core has its own key system, the Data Protection API, which encrypts authentication cookies, antiforgery tokens and anything else the app protects with it. It manages and rotates keys automatically. One setting catches almost every multi-instance deployment: the key ring must be shared and persisted, for example in blob storage protected by Key Vault. Otherwise each instance, and each restart, makes its own keys, and users are signed out whenever a request lands on another instance.

### Secrets

A **secret** is any value that grants access: database passwords and connection strings, API keys, client secrets, private keys, signing keys and webhook keys. Secrets leak through a few predictable routes, and each has a standard defence.

Source control is the most common. A secret committed once stays in the repository's history forever, even after the file is changed, and repositories get cloned, forked and occasionally made public by mistake. Bots scan public repositories for keys within minutes of a push. Keep secrets out of code and configuration files entirely, and use **secret scanning** in the repository and pipeline, which detects secret-like values before and after they are committed.

For development, `dotnet user-secrets` stores secrets in your user profile, outside the project folder, where the configuration system from What a backend does (8.1) finds them. In production, read them from a secret store such as Azure Key Vault or AWS Secrets Manager, through the same configuration system. Environment variables are the traditional middle ground; they are easy, but they can leak through process listings, crash dumps, diagnostic pages and logs.

The best secret is no secret. A **managed identity** is an identity the cloud platform assigns to a service, such as an Azure App Service or a container app. The service gets tokens for the database, the key vault and other services from the platform, through the client credentials idea in OAuth 2 and OpenID Connect (10.3), with no password or key stored anywhere in the app or its configuration. Nothing can leak, and nothing needs rotating.

Whatever remains, treat it with least privilege, from Thinking like an attacker (10.1). Give each secret the narrowest rights possible, one per service and environment, with an expiry date. Log access to it, and never log the secret itself. When a secret leaks, rotate it immediately, since deleting the commit doesn't help. Then check the logs for its use.

### Protecting data at rest and in transit

Use TLS for every connection, including inside your own network, which the zero-trust idea from Thinking like an attacker (10.1) requires. Encrypt storage at rest: managed databases and cloud storage do it by default, and SQL Server's Transparent Data Encryption covers files and backups. That protects against stolen disks and backup files, but not against an attacker who gets in through the application, who sees decrypted data. For the most sensitive fields, such as passport numbers and payment details, add column-level encryption: SQL Server's Always Encrypted, or application-level AES-GCM with keys in a vault, so even database administrators see only ciphertext. Best of all, following Thinking like an attacker (10.1), don't keep data you don't need. Card payments, for instance, are usually handled by a payment provider that returns a token, so the full card number never touches your systems.

### In short

Choose standard building blocks; never design your own. Hashes such as SHA-256 fingerprint data; HMACs add a key to prove origin, checked with constant-time comparison. Store passwords with a slow, salted password hash, such as Argon2id, or PBKDF2 at 600,000 iterations, and rehash as parameters rise. Encrypt with authenticated encryption, AES-GCM, and never reuse a nonce with a key. Sign with private keys and verify with public ones, and plan for post-quantum algorithms with crypto agility. Use `RandomNumberGenerator` for anything secret, and remember encoding isn't encryption. Keep keys in a vault, with envelope encryption for bulk data and a shared Data Protection key ring. Keep secrets out of code, scan for them, prefer managed identities, and rotate on any leak.

### What goes wrong

**Fast hashes for passwords.** SHA-256 or MD5 password hashes are cracked by the million after a leak. Use a password hashing function with a salt and a work factor.

**Encryption without authentication.** AES in older modes hides data but lets attackers alter it unnoticed. Use AES-GCM or another authenticated mode.

**Reused nonces and hard-coded keys.** A fixed nonce or a key in source code breaks everything built on it. Generate nonces properly and keep keys in a vault.

**Comparing secrets with `==`.** Timing differences leak valid MACs and tokens. Use `CryptographicOperations.FixedTimeEquals`.

**`System.Random` for tokens.** Predictable reset tokens and ids let attackers guess them. Use `RandomNumberGenerator`.

**Secrets in the repository.** A connection string committed years ago is still in the history. Scan, move secrets to a store, and rotate anything ever committed.

**Unshared Data Protection keys.** Each instance makes its own keys, and users are signed out at random. Persist and share the key ring.

**Base64 treated as protection.** Encoded passport numbers are readable by anyone. Encrypt what must be confidential.

### Cost model

SHA-256 and AES-GCM run at gigabytes per second on modern processors with hardware support, so hashing and encrypting ordinary records costs microseconds. HMACs cost about as much as a hash. A signature takes tens of microseconds to create or verify with elliptic curves; RSA signing is slower. Password hashing is deliberately expensive, around 100 to 300 ms of CPU per check at recommended settings, which is why sign-in endpoints need rate limits. A key vault call costs a network round trip and has request limits, so cache secrets for a while, and use envelope encryption to avoid calling the vault for every record.

### In other languages (optional)

**Node.js.** The built-in `crypto` module provides hashes, HMACs, `scrypt` and PBKDF2, AES-GCM, signatures, `randomBytes` and `timingSafeEqual`; `argon2` and `bcrypt` packages add those algorithms. Browsers offer the Web Crypto API.

**Python.** `hashlib`, `hmac` and `secrets` are built in; the `cryptography` package provides AES-GCM, signatures and more. The example's fixed values were checked with `hashlib` and `hmac`.

**Libraries for everyone.** libsodium, and its bindings in many languages, offers a small set of hard-to-misuse functions for encryption, signatures and password hashing, a good choice when a platform's own APIs are low-level.

### Key terms

- **Cryptographic hash**: A one-way function that turns data into a fixed-size fingerprint that changes completely if the data changes.
- **Collision resistance**: The property that nobody can find two different inputs with the same hash.
- **Message authentication code**: A tag computed from data and a secret key, proving the data came from a key holder unchanged. Often shortened to MAC.
- **HMAC**: A message authentication code built from a hash function, such as HMAC-SHA256.
- **Timing attack**: Working out a secret from how long operations take.
- **Constant-time comparison**: Comparing values in a way that always takes the same time, so timing reveals nothing.
- **Password hashing function**: A deliberately slow, salted hash designed for storing passwords, such as Argon2 or PBKDF2.
- **Work factor**: The adjustable cost, such as an iteration count, of a password hashing function.
- **Salt**: A random value stored with each password hash so identical passwords hash differently.
- **AES**: The Advanced Encryption Standard, the symmetric cipher used almost everywhere.
- **Authenticated encryption**: Encryption that also detects any change to the ciphertext, such as AES-GCM.
- **Nonce**: A value used only once with a given key, such as the 12-byte input to AES-GCM.
- **Cryptographically secure random number generator**: A generator whose output can't be predicted, suitable for keys and tokens. Often shortened to CSPRNG.
- **Key vault**: A service that stores keys and secrets, often in hardware security modules, and controls and logs their use.
- **Envelope encryption**: Encrypting data with a data key, and encrypting that data key with a master key kept in a vault.
- **Secret**: Any value that grants access, such as a password, API key, connection string or private key.
- **Secret scanning**: Automatically detecting secrets committed to repositories or present in build output.
- **Managed identity**: An identity a cloud platform gives a service so it can get tokens without any stored secret.
- **Post-quantum cryptography**: Algorithms designed to resist attacks by future quantum computers, such as ML-KEM and ML-DSA.

### Where this leads

This ends Part 10. Part 11 is about shipping software, starting with Version control with Git (11.1), which names everything by its hash. Keeping secrets out of pipelines and scanning builds are in CI/CD pipelines (11.4), and managed identities and key vaults in practice are in Cloud basics (11.5).

### Check yourself

1. Run `crypto.cs` and check that the three fixed values match the comments. Then change one character of the booking text and compare the new SHA-256 with the old one character by character.
2. Time `Rfc2898DeriveBytes.Pbkdf2` at 10,000, 100,000 and 600,000 iterations, and work out how many guesses per second one core allows at each.
3. Write a function that encrypts a string with AES-GCM and returns one value containing a version byte, the nonce, the tag and the ciphertext, and a matching decrypt function.
4. Store a connection string with `dotnet user-secrets set` in a test project, and read it through `IConfiguration` without it appearing in any file in the project folder.
5. Run a secret scanner, such as gitleaks, over the full history of a repository you work on, and decide what to rotate if it finds anything.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Passwords hashed with SHA-256

A legacy table stores SHA-256(password + salt). A GPU can try billions of guesses per second against it.

*Rehash on next login*

```csharp
var hasher = new PasswordHasher<User>();   // PBKDF2, salted, iterations rise with each .NET version

var legacyOk = user.LegacySha256 is not null && LegacyCheck(user, password);
if (legacyOk)
{
    user.PasswordHash = hasher.HashPassword(user, password);   // upgrade transparently
    user.LegacySha256 = null;
}
else
{
    var r = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
    if (r == PasswordVerificationResult.SuccessRehashNeeded) user.PasswordHash = hasher.HashPassword(user, password);
}
```

> **Lesson:** Passwords need a deliberately slow, salted hash. Upgrade old hashes when users next sign in.

#### Example 2: Everyone logged out on every deploy

After each deploy, cookies and anti-forgery tokens become invalid. The Data Protection key ring lives in the container and is regenerated.

*Persist and protect the key ring*

```csharp
builder.Services.AddDataProtection()
    .SetApplicationName("bookings")                                        // same on every instance
    .PersistKeysToAzureBlobStorage(new Uri(cfg["DataProtection:BlobUri"]!), new DefaultAzureCredential())
    .ProtectKeysWithAzureKeyVault(new Uri(cfg["DataProtection:KeyUri"]!), new DefaultAzureCredential());
```

> **Lesson:** Data Protection keys must be shared and persisted, or every restart invalidates what the app issued.

### Cryptography & Secrets Handling

<p class="src">From <em>.NET Backend Deep Dive</em> · Cryptography & Secrets Handling</p>

<a id="dn-216"></a>

#### Hash, HMAC, or encrypt?

You will not implement a cipher. You will, repeatedly, have to choose between hashing and encryption, store a credential, and rotate a key — and those are where the real mistakes happen.

| Goal | Use | Never |
|---|---|---|
| Store a password | **Argon2id** or PBKDF2 with high iterations, per-user salt | SHA-256, MD5, or any fast hash |
| Verify integrity / a webhook signature | **HMAC-SHA256** with a shared secret | A plain hash of the payload |
| Protect data you must read back | **AES-GCM** (authenticated encryption) | AES-CBC without a MAC |
| Compare secrets | `CryptographicOperations.FixedTimeEquals` | `==` (timing attack) |
| Generate a token, salt, or id | `RandomNumberGenerator` | `Random`, `Guid.NewGuid()` for security |

**Fast hashes are the wrong tool for passwords precisely because they're fast** — a GPU tries billions of SHA-256 candidates per second. Password hashes are deliberately slow and memory-hard. In practice, use ASP.NET Core Identity's hasher rather than assembling this yourself.

```csharp
// Authenticated encryption: confidentiality AND tamper detection in one construction
public static byte[] Encrypt(byte[] plaintext, byte[] key)
{
    var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
    var cipher = new byte[plaintext.Length];
    var tag = new byte[AesGcm.TagByteSizes.MaxSize];

    using var aes = new AesGcm(key, tag.Length);
    aes.Encrypt(nonce, plaintext, cipher, tag);

    return [.. nonce, .. tag, .. cipher];   // nonce and tag are not secret
}
```

**The cardinal rule of GCM: never reuse a nonce with the same key.** Doing so doesn't just weaken it — it leaks the plaintext relationship and can expose the authentication key. Generate it randomly per message, or use a counter you're certain never repeats.

<a id="dn-217"></a>

#### Don't hand-roll what the framework gives you

```csharp
// ASP.NET Core Data Protection: key management, rotation and expiry handled for you
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(blobUri, credential)   // shared across instances
    .ProtectKeysWithAzureKeyVault(keyUri, credential)     // keys encrypted at rest
    .SetApplicationName("order-api");                     // MUST match across apps

var protector = _provider.CreateProtector("EmailConfirmation");
var token = protector.Protect(userId.ToString());
```

Two failure modes worth knowing: in a container without persisted keys, **the key ring is regenerated on every restart**, so every previously issued cookie, antiforgery token and protected payload becomes invalid — the "everyone gets logged out on deploy" bug. And a mismatched `SetApplicationName` between instances produces the same symptom intermittently.

<a id="dn-218"></a>

#### Key management

- **Key rotation must not require a redeploy.** Read keys through a secret client or `IOptionsMonitor` (Part 21.2), never a captured string.
- **Support two keys at once during rotation**: decrypt with old or new, encrypt with new. Without an overlap window, rotation is an outage.
- **Envelope encryption** for data at rest: a per-record data key encrypted by a master key in a KMS. Rotating the master then re-wraps keys instead of re-encrypting terabytes.
- Assume any secret that reached a log, a crash dump, a CI variable print, or a git commit is **compromised** — rotate it, even if you amended the commit away.

<a id="dn-219"></a>

#### What to encrypt in a database, and what that costs

Encrypting a column means you **lose indexing, sorting, range queries and joins** on it. Deterministic encryption keeps equality lookups but leaks which rows share a value, which is often enough to de-anonymize.

Decide deliberately: TDE / disk-level encryption protects against a stolen disk or backup file and costs nothing in queryability. Column-level encryption protects against a compromised database *reader* and costs you the ability to query that column. Most teams need the first; far fewer genuinely need the second.

For things you only ever need to *check*, store a **keyed hash** instead: searchable by exact value, unrecoverable if leaked.

---

### Configuration & the Options Pattern

<p class="src">From <em>.NET Backend Deep Dive</em> · Configuration & the Options Pattern</p>

<a id="dn-99"></a>

#### Secrets

```csharp
// Development only - stored outside the repo, in the user profile
dotnet user-secrets set "ConnectionStrings:Default" "Server=..."

// Production: never in appsettings.json, never in environment variables
// if you can avoid it (they leak into crash dumps, /proc, and `docker inspect`)
builder.Configuration.AddAzureKeyVault(vaultUri, new DefaultAzureCredential());
```

Rules that matter in review: no secrets in source control, no secrets in container images, no secrets in log output, and rotation must not require a redeploy (which means reading them through `IOptionsMonitor` or a secret client, not a captured string).

---

### Security, Testing & Observability

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Security, Testing & Observability</p>

<p class="pd">The three disciplines that separate a demo from a system somebody trusts: proving who the caller is, proving the code works, and being able to see what it is doing at three in the morning.</p>

<a id="be-a187"></a>

#### Secrets, keys and data protection

1.  Nothing secret in source control, ever — including in appsettings, test fixtures and commit history.
2.  Local development: `dotnet user-secrets`, which stores outside the repo.
3.  Deployed: a managed store — Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, or Kubernetes secrets backed by one of those.
4.  Prefer workload identity over any stored credential: the platform proves who the pod is, and no secret exists to leak.
5.  Rotate on a schedule and after every person leaves the team; make sure rotation does not require a deploy.

``` text
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://{vaultName}.vault.azure.net/"),
    new DefaultAzureCredential());        // managed identity in prod, your login locally
```

##### Hashing and encryption, briefly

| Need                               | Use                                                  | Never use                                         |
|------------------------------------|------------------------------------------------------|---------------------------------------------------|
| Store a password                   | Argon2id, or ASP.NET Core Identity's PBKDF2 defaults | MD5, SHA-1, unsalted anything                     |
| Encrypt data at rest               | AES-GCM via `AesGcm`, keys in a vault                | Hand-rolled ECB or a hardcoded key                |
| Sign / verify                      | HMAC-SHA256, or RSA/ECDSA for public verification    | Comparing with `==` — use a constant-time compare |
| Protect cookies and tokens locally | `IDataProtectionProvider`                            | Rolling your own                                  |

> **\[Trap\]**
>
> **Common trap.** Data Protection keys default to the local filesystem. In a container they are lost on restart and differ per replica, so auth cookies and antiforgery tokens break intermittently. Persist keys to a shared store (`PersistKeysToAzureBlobStorage`, Redis, or a mounted volume) and encrypt them at rest.

###### Worked example

A rotation that does not require a deploy, which is the property that makes rotation actually happen.

``` csharp
// wrong: the secret is read once at startup and captured
var apiKey = builder.Configuration["Partner:ApiKey"];
builder.Services.AddSingleton(new PartnerClient(apiKey));       // rotation = restart

// right: read it at the point of use, through IOptionsMonitor
builder.Services.Configure<PartnerOptions>(builder.Configuration.GetSection("Partner"));
builder.Services.AddSingleton<PartnerClient>();

public class PartnerClient(IOptionsMonitor<PartnerOptions> options, HttpClient http)
{
    public async Task<T> CallAsync<T>(string path, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("X-Api-Key", options.CurrentValue.ApiKey);   // current value, every call
        ...
    }
}

// and make the configuration provider reload
builder.Configuration.AddAzureKeyVault(vaultUri, new DefaultAzureCredential(),
    new AzureKeyVaultConfigurationOptions { ReloadInterval = TimeSpan.FromMinutes(5) });
```

``` text
  a rotation with overlap, so nothing breaks mid-flight

  t+0     issue KEY-2 alongside KEY-1; the partner now accepts BOTH
  t+5m    Key Vault updated to KEY-2; app config providers reload within their interval
  t+15m   verify: logs show only KEY-2 in use (log the key ID, never the key)
  t+24h   revoke KEY-1 at the partner
  t+24h   alert if any request still attempts KEY-1  -> something was missed

  the overlap window is what makes this a non-event. Rotating without one means
  a synchronized cutover across every replica, which is a coordinated outage.
```

###### Data Protection in containers, the failure everyone hits

ASP.NET Core encrypts antiforgery tokens, auth cookies and `[TempData]` with keys from the Data Protection API. By default those keys are generated on first use and stored on the local filesystem.

``` text
  what that means in Kubernetes with three replicas

  pod A generates key-set A     ->  issues a cookie encrypted with A
  pod B generates key-set B     ->  cannot decrypt A's cookie
  load balancer sends the next request to pod B
  -> "The antiforgery token could not be decrypted" / user is silently logged out
  -> reproduces roughly 2 requests in 3, but only sometimes, so it looks intermittent
  -> and a pod restart loses key-set A entirely, logging out everyone it served
```

``` csharp
// persist and encrypt the key ring - do this on day one, not after the incident
builder.Services.AddDataProtection()
    .SetApplicationName("orders")                       // MUST match across all replicas
    .PersistKeysToAzureBlobStorage(blobUri, new DefaultAzureCredential())
    .ProtectKeysWithAzureKeyVault(keyUri, new DefaultAzureCredential())
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// alternatives
    .PersistKeysToStackExchangeRedis(multiplexer, "DataProtection-Keys")
    .PersistKeysToFileSystem(new DirectoryInfo("/var/keys"))   // a shared PersistentVolume
```

> **\[Trap\]**
>
> **Common trap.** `SetApplicationName` matters as much as the storage. The application name is part of the key derivation, so two services sharing a key ring but with different names cannot read each other's payloads — which is correct isolation, and also the cause of the ‘we configured shared storage and it still does not work’ follow-up ticket.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a199"></a>

#### Identity infrastructure: stop storing credentials

The strongest secret management is having no secret. Managed identity (Azure), IAM roles for service accounts (AWS), and workload identity (Kubernetes) let the platform vouch for the workload, and the SDK exchanges that for a short-lived token.

``` csharp
// identical code locally and in production
var credential = new DefaultAzureCredential();
// local: your az login / Visual Studio identity
// prod : the pod's federated workload identity - no secret exists anywhere

var blobs = new BlobServiceClient(new Uri($"https://{account}.blob.core.windows.net"), credential);
var vault = new SecretClient(new Uri(vaultUri), credential);

// even the database
builder.Services.AddDbContext<AppDb>(o =>
    o.UseSqlServer("Server=sql.database.windows.net; Database=orders; Authentication=Active Directory Default"));
```

``` text
  pod  --(projected service account token)-->  cloud identity provider
       <--(short-lived access token, ~1h)-----
       --(Bearer token)-->  SQL / Key Vault / Storage / Service Bus

  nothing to rotate, nothing to leak, every access attributable to a workload identity
```

> **\[Note\]**
>
> **Least privilege, actually applied.** Grant the workload identity the narrowest role that works — `Storage Blob Data Reader` on one container, not Contributor on the subscription. Review these quarterly; permissions accumulate and nobody ever removes one.

###### The credential chain, and what it tries in order

``` csharp
// one line, and it behaves correctly in every environment
var credential = new DefaultAzureCredential();
var client = new SecretClient(new Uri(vaultUrl), credential);

// what it actually attempts, in order, stopping at the first that works:
//   1. environment variables        (AZURE_CLIENT_ID / SECRET / TENANT_ID)
//   2. workload identity            (federated token in Kubernetes)
//   3. managed identity             (the instance metadata endpoint)
//   4. Visual Studio / VS Code      (your signed-in developer account)
//   5. Azure CLI  (az login)        <- how it works on your machine
//   6. Azure PowerShell
//
// so the SAME CODE uses your developer identity locally and a managed
// identity in production, with no secret in either case.

// in production, be explicit - the fallback chain hides misconfiguration
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ManagedIdentityClientId = cfg["ManagedIdentityClientId"],
    ExcludeAzureCliCredential = !env.IsDevelopment(),
    ExcludeVisualStudioCredential = !env.IsDevelopment(),
});
```

``` text
// the destination: NO connection string secret at all
// Azure SQL / PostgreSQL with a managed identity
"ConnectionStrings:Default": "Server=db.database.windows.net;Database=app;Authentication=Active Directory Default;"

// AWS equivalent: IAM database authentication + IRSA on EKS
// GCP equivalent: Cloud SQL IAM authentication + Workload Identity

// nothing to store, nothing to rotate, nothing to leak, and every access
// is attributable to a specific workload identity in the audit log.
```

> **\[Trap\]**
>
> **Common trap.** `DefaultAzureCredential`'s fallback chain is excellent for development and a liability in production: if the managed identity is misconfigured, it silently falls through and fails with an unhelpful aggregate error listing six attempted mechanisms. Worse, on a developer's machine it may *succeed* using their personal account, so code that would fail in production passes locally. Exclude the developer credentials outside development, and specify the identity explicitly.

<a id="be-a200"></a>

#### If you must store passwords: hashing done properly

Delegating identity is the right answer and the previous section makes that case. But internal tools, legacy migrations and air-gapped systems still end up storing credentials — and the difference between doing it properly and doing it badly is one breach.

|                                        | Use                                                        | Note                                                                   |
|----------------------------------------|------------------------------------------------------------|------------------------------------------------------------------------|
| **Argon2id**                           | First choice for new systems                               | Memory-hard: resists GPU cracking. Needs a library (Isopoh, Konscious) |
| **PBKDF2**                             | Built into .NET, FIPS-friendly                             | What ASP.NET Identity uses; raise the iteration count over time        |
| **bcrypt**                             | Fine, widely available                                     | 72-byte input limit — long passphrases silently truncate               |
| MD5, SHA-1, SHA-256, unsalted anything | Never. Fast hashes are the attacker’s advantage, not yours |                                                                        |

``` csharp
// The built-in hasher already does the right things: per-password salt,
// PBKDF2-HMAC-SHA512, version byte, and constant-time verification.
var hasher = new PasswordHasher<User>();
user.PasswordHash = hasher.HashPassword(user, plaintext);

// Verification returns THREE states, and the third one matters:
var result = hasher.VerifyHashedPassword(user, user.PasswordHash, attempt);
switch (result)
{
    case PasswordVerificationResult.Failed:
        return Unauthorized();
    case PasswordVerificationResult.SuccessRehashNeeded:
        // stored with older/weaker parameters — upgrade it now, while we
        // legitimately hold the plaintext. This is the ONLY moment you can.
        user.PasswordHash = hasher.HashPassword(user, attempt);
        await db.SaveChangesAsync(ct);
        break;
    case PasswordVerificationResult.Success:
        break;
}

// Raising the work factor as hardware improves:
services.Configure<PasswordHasherOptions>(o =>
{
    o.IterationCount = 600_000;          // OWASP guidance moves — check it
});   // existing hashes keep working; they upgrade on next successful login
```

``` csharp
// Timing: never let the response time reveal whether the account exists
var user = await users.FindByEmailAsync(email);
if (user is null)
{
    hasher.VerifyHashedPassword(dummyUser, DummyHash, attempt);  // burn the time
    return GenericFailure();                                      // same message
}

// Lockout, so an online guess is not a free infinite loop
services.Configure<IdentityOptions>(o =>
{
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(15);
    o.Password.RequiredLength = 12;      // length beats character classes
    o.Password.RequireNonAlphanumeric = false;   // complexity rules push
});                                      // users toward Passw0rd! patterns
```

> **\[Trap\]**
>
> **Common trap.** Pepper — a secret added to every hash — is sometimes proposed as extra protection. It only helps if it lives somewhere the database dump does not (an HSM, a separate key store), and it makes rotation genuinely hard. A per-password salt plus a memory-hard algorithm is the part that actually matters; get that right before adding ceremony.

> **\[Trap\]**
>
> **Password rules that hurt.** Forced 90-day rotation, character-class requirements and short maximum lengths all measurably reduce security by pushing people to predictable patterns and reuse. Modern guidance: long minimum, no forced expiry without evidence of compromise, no arbitrary maximum below 64 characters, and check candidates against a breached-password list.

### Security in Depth

<p class="src">From <em>Backend Engineering</em> · APIs · Security in Depth</p>

<p class="pd">Authentication and authorization were Part IV. These are the rest.</p>

<a id="be-e539"></a>

#### Secrets

- Never in source, never in query strings, never in log output
- Environment variables or a secret manager (Key Vault, Secrets Manager)
- Rotate on a schedule and after any suspected exposure
- Prefix API keys (`sk_live_`) so automated secret scanners can find leaks
- Support two valid keys at once, or rotation requires downtime

------------------------------------------------------------------------

### Platform Security

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Platform Security</p>

<p class="pd">Security as a property of the deployed system rather than the code: identity, secrets, patching and the posture that limits what a compromise can reach.</p>

<a id="be-g712"></a>

#### Identity, and eliminating credentials

``` text
  the progression, worst to best

  1  a password in a config file, committed to git
  2  a password in an environment variable, set by the pipeline
  3  a secret in a vault, fetched at startup with a bootstrap credential
  4  WORKLOAD IDENTITY - the platform vouches for the workload, and the
     resource accepts a short-lived token. NO SECRET EXISTS AT ALL.

  4 is available on every major cloud and is genuinely simpler than 3
  once configured. Nothing to rotate, nothing to leak, nothing to audit
  the storage of - because there is nothing stored.
```

| Platform             | Mechanism                                                        |
|----------------------|------------------------------------------------------------------|
| AWS                  | IAM roles for EC2/ECS; IRSA or Pod Identity for EKS              |
| Azure                | Managed identity; workload identity for AKS                      |
| GCP                  | Service accounts; workload identity for GKE                      |
| Kubernetes generally | ServiceAccount tokens, projected and short-lived                 |
| Between services     | mTLS with certificates from an internal CA, or SPIFFE identities |
| Databases            | IAM database authentication, where supported                     |

``` csharp
# what this looks like in practice - no connection string secret at all
# Azure:
"ConnectionStrings:Default": "Server=db.postgres.database.azure.com;Database=app;"
# the token comes from the managed identity at runtime:
var credential = new DefaultAzureCredential();
var token = await credential.GetTokenAsync(
    new TokenRequestContext(new[] { "https://ossrdbms-aad.database.windows.net/.default" }));
# tokens last ~1 hour and refresh automatically. Nothing to rotate.
```

- **Least privilege, per workload.** A service that reads one bucket gets read on one bucket — not a shared role with broad access.
- **No standing human access to production.** Just-in-time elevation, time-boxed, logged, alerted.
- **Separate the pipeline's identity from the application's.** The deployer needs to change infrastructure; the application does not.
- **Separate environments completely.** A development credential must have no path to production.
- **Audit and review permissions.** Grants accumulate; nobody removes them unless a process does.
- **MFA everywhere for humans**, including the cloud console and the source repository.

###### OIDC federation from a pipeline, and why it replaces stored keys

``` text
  the old way                        the federated way

  store AWS_ACCESS_KEY_ID and        the CI provider issues a signed OIDC
  AWS_SECRET_ACCESS_KEY as CI        token asserting "this is repo acme/app,
  secrets                            branch main, workflow deploy.yml"
                                       |
  long-lived, rarely rotated,        the cloud trusts that issuer and
  readable by anyone who can         exchanges the token for CREDENTIALS
  modify the pipeline, and           VALID FOR ONE HOUR, scoped by a
  present in every fork's build      condition on the token's claims
  if you are not careful               |
                                     nothing is stored. Nothing to leak.
                                     Nothing to rotate.
```

``` sql
# GitHub Actions -> AWS, with no stored credentials
permissions:
  id-token: write            # allow the workflow to request an OIDC token
  contents: read
steps:
  - uses: aws-actions/configure-aws-credentials@v4
    with:
      role-to-assume: arn:aws:iam::123456789012:role/deploy
      aws-region: eu-west-1

# and the trust policy, which is where the real security lives:
{
  "Condition": {
    "StringEquals": {
      "token.actions.githubusercontent.com:sub": "repo:acme/app:ref:refs/heads/main"
    }
  }
}
# note the exactness. A condition of "repo:acme/*" would let ANY repository
# in the org assume the production deploy role - including a new one created
# by anyone with repo-creation rights.
```

| Pipeline risk                             | Control                                                                                    |
|-------------------------------------------|--------------------------------------------------------------------------------------------|
| Secrets exposed to fork PR builds         | Environment protection rules; never expose production secrets to `pull_request` from forks |
| Overly broad trust condition              | Pin the subject to a branch or environment, not a wildcard                                 |
| Pipeline definition changed to exfiltrate | Require review on workflow files; use CODEOWNERS                                           |
| Third-party action compromised            | Pin actions by commit SHA, not by tag                                                      |
| Build agent reaching anywhere             | Restrict egress from runners                                                               |
