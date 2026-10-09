---
id: "11-4"
number: "11.4"
title: "CI/CD pipelines"
part: 11
part_title: "Shipping software"
words: 10837
minutes: 47
---

# 11.4 CI/CD pipelines

> **Quick look.** Pipelines that build, test and release every small change the same way.
>
> - CI builds and tests every merge; delivery keeps every change releasable; deployment releases automatically.
> - Check each PR: locked restore, warnings as errors, tests, analysis and scans; keep it fast and free of flaky tests.
> - Build one artifact and promote it by digest through environments; smoke-test after each deploy.
> - Rolling deploys need old and new code to coexist, so run expand-contract migrations first.
> - Liveness must not check the database, or one blip restarts every pod; add a short preStop pause.
> - Feature flags separate deploy from release; delete flags when done.
> - Use federated identity, least-privilege pipelines and pinned actions; track DORA metrics.


### The big picture

Organizations that release software well do it often and without drama. Small changes flow from a merged pull request to production in hours, through the same automated steps every time, and a bad change is noticed and undone in minutes. Organizations that release badly do it rarely, in big batches, with long checklists, late nights and a code freeze beforehand, and each release is risky precisely because it contains so much. The difference is mostly automation: a pipeline that builds, tests and deploys every change the same way.

This chapter covers continuous integration, continuous delivery and continuous deployment, and what a good pipeline checks. It explains building one artifact and promoting it through environments. It covers the deployment strategies that make releases safe, from rolling updates to canaries and feature flags, and how database migrations fit in. It also covers the pipeline's own security, and the four measures that show how well a team delivers. The example is a complete GitHub Actions workflow for the bookings API: pull request checks, one image built and pushed, deployment by digest to staging, a smoke test, then production after approval, all without storing any cloud credentials. It was checked with actionlint, a linter for workflows. The chapter also shows the mistake actionlint caught when a job dependency was removed.

### Before you start

**Builds on:** 11.1 Version control with Git, 11.2 Builds and dependencies, 4.6 Testing, 10.5 Secrets and cryptography basics, 11.3 Containers and Docker, 8.1 What a backend does.

**Ideas explained later:**

- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Monitoring** (11.6): Watching a system for known problems, such as an error rate above a threshold.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Observability** (11.6): Being able to explain a system's behaviour, including new questions, from the signals it emits.
- **Performance** (12.4): How quickly a system does its work at a given load.
- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Incident** (12.5): An unplanned disruption that affects users or threatens to.

**Tools and technologies named here:**

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between continuous delivery and continuous deployment?**

> With continuous delivery, every change that passes the pipeline is ready to release, and a person decides when. With continuous deployment, every change that passes goes to production automatically.

**2. What does "build once, deploy many" mean?**

> The pipeline builds one artifact per commit, such as an image identified by its digest, and promotes that same artifact through test, staging and production. Configuration differs per environment; the artifact never does.

**3. How does a canary release reduce risk?**

> The new version first gets a small share of real traffic, such as 5%. Its error rates and latency are compared with the old version, and it is promoted gradually if healthy, or withdrawn if not, before most users ever see it.

**4. What do feature flags separate?**

> Deployment from release. Code can be deployed switched off, then turned on for some or all users later, and turned off again instantly without a redeploy.

**5. How can a pipeline deploy to a cloud account without storing any credentials?**

> With workload identity federation: the pipeline gets a short-lived signed token from its own platform, over OpenID Connect, and the cloud trusts tokens from that repository and branch. No secret is stored anywhere.

**6. What are the four DORA metrics?**

> Deployment frequency, lead time for changes, change failure rate and time to restore service. Together they measure both speed and stability of delivery.

**7. Why should database migrations run before the new code is deployed, and what must they avoid?**

> So the new code finds the schema it needs. The old code is still running at that point, so migrations must not break it: they follow the expand-contract pattern, adding before removing.

### Integrating continuously

**Continuous integration** (CI) means everyone merges small changes into the main branch often, at least daily, and every change is built and tested automatically. It is the automated side of trunk-based development from Version control with Git (11.1). Problems appear within minutes of the change that caused them, while the author still remembers it, and the main branch stays in a state that works. When the main build breaks, fixing it comes before anything else.

**Continuous delivery** extends that to release: every change that passes the pipeline is deployable, and releasing is a routine, low-risk step that anyone on the team can trigger. **Continuous deployment** goes one step further: every change that passes the pipeline goes to production automatically, with no manual step. Most teams practise continuous delivery, and many move to continuous deployment for services whose tests and monitoring they trust.

### Pipelines as code

A **CI/CD pipeline** is the automated sequence of steps from commit to production. Today pipelines are written as code, in files that live in the repository and are reviewed like any other change. GitHub Actions, Azure Pipelines and GitLab CI use **YAML**, a text format for structured data that uses indentation instead of brackets. It is easy to read, and sensitive to exact indentation and quoting, which is one reason to lint pipeline files.

The vocabulary is similar everywhere. A trigger starts the pipeline, such as a pull request, a push to main, a schedule or a manual start. The pipeline is made of jobs, which can run in parallel or depend on each other, and each job is a list of steps: shell commands or reusable actions. Jobs run on a **runner**, a machine or container provided by the service or by you. A **build artifact** is a file a job produces and hands on, such as test results, a published folder or a container image. Caches keep downloaded packages between runs. **Deployment environments** such as staging and **production**, the one that real users and data depend on, group the settings, secrets and protection rules, such as required approvers, for each place the software runs.

### What the pipeline checks

On every pull request, a good pipeline answers one question: is this change safe to merge?

It restores dependencies in locked mode and builds with warnings as errors, from Builds and dependencies (11.2). It runs the unit tests and the integration tests, with real databases in containers through Testcontainers, from Testing (4.6). It runs static analysis, and scans dependencies, container images and the change itself for known vulnerabilities and committed secrets, from Secrets and cryptography basics (10.5). It lints configuration such as pipeline files, Dockerfiles and infrastructure templates. Branch protection makes these checks required, so nothing merges without them.

Speed matters as much as thoroughness. A pipeline that takes an hour is worked around; one that takes ten minutes is used. Run independent jobs in parallel, cache packages, build only what changed in large repositories, and put the slowest tests in a later stage. A test that fails at random, a flaky test, destroys trust in the whole pipeline, because people learn to rerun failures instead of reading them. Fix it, or quarantine it and track it, the same week.

### Build once, deploy many

On the main branch, the pipeline builds the deployable artifact exactly once: here, a container image tagged with the commit and identified by its digest, from Containers and Docker (11.3). That same artifact is then promoted: deployed to staging, checked, then deployed to production. Nothing is rebuilt along the way. Everything that differs between environments, such as connection strings, service addresses, feature switches and scale, comes from configuration and secrets per environment, as What a backend does (8.1) described. What reaches production is then exactly what was tested.

After each deployment, a **smoke test** checks that the service really started and answers, typically by calling its readiness endpoint and one or two critical paths. It catches failures that tests couldn't, such as a missing setting or a firewall rule, in seconds.

### Seeing it for real

This workflow builds and tests every pull request. On main, it also builds and pushes the image once, deploys it by digest to staging with a smoke test, and deploys the same image to production once someone approves it. It signs in to Azure with a short-lived identity token rather than a stored secret, as explained further down. It uses the files from Builds and dependencies (11.2) and Containers and Docker (11.3), and goes in `.github/workflows/`.

```yaml
name: bookings-api

on:
  pull_request:                      # every pull request: build and test
  push:
    branches: [main]                 # every merge to main: also build the image and deploy

permissions:
  contents: read                     # least privilege by default; jobs ask for more only if they need it

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: global.json        # the SDK pinned in 11.2
          cache: true                          # reuse downloaded packages between runs
          cache-dependency-path: "**/packages.lock.json"
      - run: dotnet restore --locked-mode
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build --logger trx --results-directory TestResults
      - name: Fail on vulnerable packages
        run: |
          dotnet list package --vulnerable --include-transitive | tee vulnerable.txt
          ! grep -q "has the following vulnerable packages" vulnerable.txt
      - uses: actions/upload-artifact@v4
        if: always()                           # keep test results even when tests fail
        with:
          name: test-results
          path: TestResults

  image:
    needs: build
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write                          # push to GitHub Container Registry
    outputs:
      digest: ${{ steps.push.outputs.digest }}
    steps:
      - uses: actions/checkout@v4
      - uses: docker/setup-buildx-action@v3
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - id: push
        uses: docker/build-push-action@v6
        with:
          context: .
          push: true
          tags: ghcr.io/${{ github.repository }}:${{ github.sha }}   # a tag that is never reused

  deploy-staging:
    needs: image
    runs-on: ubuntu-latest
    environment: staging
    permissions:
      contents: read
      id-token: write                          # lets the job prove its identity to Azure: no stored cloud secret
    steps:
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - name: Deploy the tested image, by digest
        run: |
          az config set extension.use_dynamic_install=yes_without_prompt
          az containerapp update --name bookings-api --resource-group rg-bookings-staging \
            --image "ghcr.io/${{ github.repository }}@${{ needs.image.outputs.digest }}"
      - name: Smoke test
        run: curl --fail --retry 10 --retry-delay 6 --retry-all-errors https://bookings-staging.airline.example/health/ready

  deploy-production:
    needs: [image, deploy-staging]
    runs-on: ubuntu-latest
    environment: production                    # the environment's protection rules require an approval
    permissions:
      contents: read
      id-token: write
    steps:
      - uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - name: Deploy the same image
        run: |
          az config set extension.use_dynamic_install=yes_without_prompt
          az containerapp update --name bookings-api --resource-group rg-bookings-production \
            --image "ghcr.io/${{ github.repository }}@${{ needs.image.outputs.digest }}"
      - name: Smoke test
        run: curl --fail --retry 10 --retry-delay 6 --retry-all-errors https://bookings.airline.example/health/ready
```

actionlint, which checks workflow syntax, expressions, job dependencies and the inputs of well-known actions, reports no problems with it. To see what it catches, remove `image` from the production job's `needs`, leaving only `deploy-staging`. The job still uses the image job's digest, and actionlint reports the mistake before anything runs:

```text
broken.yml:93:210: property "image" is not defined in object type {deploy-staging: {outputs: {}; result: string}} [expression]
```

Without the linter, that mistake would appear only at deployment time, as an empty image reference in the production deployment.

### Releasing safely

A deployment strategy decides how the new version replaces the old one.

A **rolling deployment** replaces instances a few at a time, as the Kubernetes settings in Containers and Docker (11.3) did, so capacity never drops, and old and new versions briefly run side by side. That is why every change, including schema changes, must work with both versions at once.

A **blue-green deployment** runs the new version, green, alongside the old one, blue, on its own full set of instances. Traffic switches over in one step once green is checked, and switching back is just as quick. The cost is running double capacity during the change.

A **canary release** sends a small share of real traffic, say 5%, to the new version first, and compares its error rate and latency with the old version's. If they look healthy, the share grows in steps; if not, the canary is withdrawn before most users notice. It needs good metrics, from Observability (11.6), and ideally automated analysis. The name comes from the canaries miners once carried to detect gas.

A **feature flag** is a switch in configuration that turns a piece of functionality on or off at run time, for everyone or for chosen users, regions or percentages. Flags separate deployment, putting code in production, from release, letting users use it. Unfinished work can be merged and deployed switched off, as trunk-based development requires, and a problem can be switched off instantly without a redeployment. Flags are debt: remove each one once its feature is fully released, or the code fills with branches nobody understands.

When a release goes wrong, there are two ways out. Rolling back redeploys the previous version, which is quick when the previous artifact is still available and the schema is compatible. Rolling forward deploys a fix as a new version, which small, frequent releases make practical. Either way, the faster you can deploy, the safer each deployment is.

### Database changes

Database migrations, from Schema design and migrations (7.5), run as a pipeline step before the new code deploys. The new code then finds the schema it needs. The old code, still running during a rolling update, must keep working, so every migration follows expand-contract: add columns and tables in one release, move code over, and remove the old ones in a later release. EF Core migration bundles or idempotent scripts, from ORMs and EF Core (7.7), run migrations from the pipeline with their own credentials. Test migrations against a copy of production-sized data, and never let an application start-up race to apply them.

### Securing the pipeline

A pipeline can change production, so it is one of the most valuable targets in the organization, and part of the supply chain from Builds and dependencies (11.2).

Give each pipeline and job the least privilege it needs. The example sets read-only permissions by default and grants `packages: write` or `id-token: write` only to the jobs that need them.

Don't store long-lived cloud credentials in the pipeline at all. **Workload identity federation** lets the pipeline request a short-lived token from its own platform, signed over OpenID Connect, from OAuth 2 and OpenID Connect (10.3), and the cloud trusts tokens from that repository, branch and environment. The `azure/login` steps in the example work this way, which is why they need `id-token: write` and no password. It is the pipeline's version of the managed identity from Secrets and cryptography basics (10.5).

Protect production with environment rules: required reviewers, deployments only from the main branch, and waiting periods if needed. Treat third-party actions as dependencies. Pin them to a version, or better to the commit hash of that version, and review what they do. Be careful with pull requests from forks: code from outside must never run with the repository's secrets, which is why GitHub's `pull_request_target` trigger needs great care. Signing artifacts and recording how they were built, as described by the SLSA framework, lets production refuse anything that didn't come from the pipeline.

### Measuring delivery

The DORA research programme, which studied thousands of organizations, found four measures that together describe delivery performance, now called the **DORA metrics**. Deployment frequency is how often you deploy to production. Lead time for changes is how long a commit takes to reach production. Change failure rate is the share of deployments that cause a failure needing a fix. Time to restore service is how long it takes to recover from a failure. The striking finding is that speed and stability go together: teams that deploy most often also fail least and recover fastest, because small, automated changes are easier to check and to undo.

### In short

Continuous integration builds and tests every small change merged into main; continuous delivery keeps every passing change releasable; continuous deployment releases it automatically. Pipelines are YAML in the repository, built from triggers, jobs, steps, runners, artifacts and environments. Check every pull request with locked restores, warnings as errors, tests, analysis and scans, and keep the pipeline fast and free of flaky tests. Build one artifact and promote it by digest, with smoke tests after each deployment. Release with rolling, blue-green or canary deployments and feature flags, and run expand-contract migrations first. Secure the pipeline with least privilege, federated identity, protected environments and pinned actions, and track the DORA metrics.

### What goes wrong

**Rebuilding per environment.** Production runs an artifact nobody tested. Build once and promote the same digest.

**Slow or flaky pipelines.** People stop waiting for checks and start rerunning failures. Keep pipelines fast and fix flaky tests immediately.

**Big, infrequent releases.** Months of changes ship together, and any failure is hard to find and undo. Deploy small changes often.

**Stored cloud secrets.** A long-lived deployment key leaks from a pipeline log or a compromised action. Use workload identity federation.

**Over-privileged pipelines.** Every job can write packages and deploy. Grant permissions per job.

**Unpinned third-party actions.** A compromised action update runs in your pipeline with your secrets. Pin versions or commit hashes, and review changes.

**Breaking migrations in rolling deployments.** A column is dropped while old instances still query it. Use expand-contract.

**Feature flags that never leave.** Hundreds of stale flags make behaviour impossible to predict. Remove flags once their feature is fully released.

### Cost model

A typical .NET pull request pipeline takes five to fifteen minutes: a minute or two to check out and restore, with a warm cache, a few minutes to build and run unit tests, and more for integration tests with containers. Hosted runners are billed by the minute, so caching and parallel jobs save both time and money. Image builds with cached layers take a few minutes, and a deployment with a smoke test a few more. Blue-green deployments briefly double compute costs; canaries cost little extra, but need good metrics. Against these costs, each hour of lead time removed and each failed release avoided is worth far more.

### In other languages (optional)

**Azure Pipelines.** Uses YAML with stages, jobs and steps, environments with approvals, and service connections that support workload identity federation.

**GitLab CI.** Uses a `.gitlab-ci.yml` file with stages, jobs, artifacts and environments, and OIDC tokens for cloud access.

**Every stack.** Node.js, Python and Java pipelines follow the same shape: restore from a lockfile, build, test, scan, build an image once, promote it. Only the build commands change.

**GitOps.** Tools such as Argo CD and Flux deploy by watching a repository of manifests: the pipeline updates the image digest in that repository, and the tool makes the cluster match it.

### Key terms

- **Continuous integration**: Merging small changes into main frequently, with every change built and tested automatically.
- **Continuous delivery**: Keeping every change that passes the pipeline ready to release at any time.
- **Continuous deployment**: Releasing every change that passes the pipeline to production automatically.
- **CI/CD pipeline**: The automated sequence of steps that takes a change from commit to production.
- **YAML**: A text format for structured data that uses indentation, used for pipelines and many configuration files.
- **Runner**: The machine or container that runs a pipeline's jobs.
- **Build artifact**: A file produced by a pipeline job and passed on, such as test results or a container image.
- **Deployment environment**: A place the software runs, such as staging or production, with its own settings, secrets and rules.
- **Production**: The environment that real users and real data depend on, as opposed to development, test or staging.
- **Smoke test**: A quick check after deployment that the service started and answers.
- **Rolling deployment**: Replacing instances a few at a time, so old and new versions briefly run together.
- **Blue-green deployment**: Running the new version alongside the old one and switching all traffic over at once.
- **Canary release**: Sending a small share of traffic to a new version first, and widening it only if it stays healthy.
- **Feature flag**: A run-time switch that turns functionality on or off without redeploying.
- **Workload identity federation**: Letting a pipeline get short-lived cloud access from its platform's signed tokens, with no stored secret.
- **DORA metrics**: Deployment frequency, lead time for changes, change failure rate and time to restore service.

### Where this leads

The cloud services the pipeline deploys to, and defining them as code, are in Cloud basics (11.5). The metrics that canaries and smoke tests rely on are in Observability (11.6). Keeping services available through deployments and failures is in Reliability (12.5).

### Check yourself

1. Run actionlint on the example workflow, then misspell `needs.image.outputs.digest` as `needs.image.outputs.digets` and read what it reports.
2. Add a job to the workflow that runs `dotnet ef migrations bundle` and uploads the bundle as an artifact for the deployment jobs to run.
3. Pin each third-party action in the workflow to the full commit hash of its current release, keeping the version as a comment.
4. Sketch a canary release for the bookings API: which share of traffic at each step, which metrics decide promotion, and what triggers a rollback.
5. Measure the four DORA metrics for a team you know, roughly, from its repository and incident history, and pick the one to improve first.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: "It passed in staging" but production differed

The pipeline rebuilds the app for each environment. A package resolved differently on the production build.

*Build once, promote by digest*

```yaml
# GitHub Actions (sketch)
build:
  steps:
    - run: dotnet test --configuration Release
    - run: docker build -t $REGISTRY/bookings:${{ github.sha }} . && docker push $REGISTRY/bookings:${{ github.sha }}
deploy-staging:
  needs: build
  steps: [ { run: "helm upgrade bookings ./chart --set image.tag=${{ github.sha }} -n staging" } ]
deploy-prod:
  needs: deploy-staging
  environment: production          # requires approval
  steps: [ { run: "helm upgrade bookings ./chart --set image.tag=${{ github.sha }} -n prod" } ]
```

> **Lesson:** Build one artifact and promote the same image through every environment; configuration comes from the environment.

#### Example 2: A liveness probe that restarted every pod

A 20-second database blip made every pod fail liveness. Kubernetes restarted them all, and cold pods hammered the recovering database.

*Separate liveness and readiness*

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddSqlServer(cs, tags: ["ready"]);

app.MapHealthChecks("/health/live",  new() { Predicate = r => r.Tags.Contains("live") });    // the process only
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });   // dependencies
```

> **Lesson:** Liveness asks "is this process stuck?" and must not check dependencies. Readiness decides whether to send traffic.

### CI/CD & Delivery

<p class="src">From <em>.NET Backend Deep Dive</em> · CI/CD & Delivery</p>

<a id="dn-186"></a>

#### Pipeline stages, and what each one is for

```
commit
 ├─ build + analyzers as errors + format check        (fast, <2 min)
 ├─ unit tests                                        (fast)
 ├─ integration tests (Testcontainers)                (slower, still on every PR)
 ├─ security: dependency scan, secret scan, SAST
 ├─ build container image -> push with an immutable tag (git sha, never 'latest')
 ├─ deploy to staging + smoke tests
 └─ deploy to production (gated) -> canary -> full rollout
```

**Build the artifact once and promote it.** Rebuilding per environment means production runs a binary nobody tested. Configuration comes from the environment (Part 21.1), never from a rebuild.

**Immutable tags.** `latest` makes rollback ambiguous and deploys non-reproducible. Tag with the commit SHA.

**Fail fast, in cost order**: compile → analyzers → unit → integration → E2E. A ten-second failure beats the same failure after eleven minutes.

<a id="dn-188"></a>

#### Deploy ≠ release

**Feature flags** decouple the two, which is what makes trunk-based development and continuous deployment safe:

```csharp
if (await _flags.IsEnabledAsync("new-pricing-engine", user))
    return await _newPricing.CalculateAsync(cart);
return await _legacyPricing.CalculateAsync(cart);
```

Deploy dark → enable for internal users → 1% → 50% → 100% → **delete the flag**. That last step is the one teams skip; stale flags become permanent branching complexity, and a flag that's been at 100% for six months is just dead code with extra steps. Put an expiry date on every flag at creation.

### Health Checks, Deployment & Rollout

<p class="src">From <em>.NET Backend Deep Dive</em> · Health Checks, Deployment & Rollout</p>

<a id="dn-123"></a>

#### Liveness vs readiness — different questions

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddDbContextCheck<AppDbContext>(tags: ["ready"])
    .AddRedis(redisCs, tags: ["ready"])
    .AddUrlGroup(new Uri("https://payment.internal/health"), tags: ["ready"]);

app.MapHealthChecks("/health/live",  new() { Predicate = c => c.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
```

| Probe | Question | Failure means |
|---|---|---|
| **Liveness** | Is the process wedged? | **Restart the pod** |
| **Readiness** | Can it serve traffic right now? | Remove from the load balancer, don't restart |
| **Startup** | Has it finished booting? | Suppresses the other probes during slow startup |

**The most damaging mistake here** is checking the database in the *liveness* probe. The database blips, every pod reports unhealthy, Kubernetes restarts all of them simultaneously, they come back with empty caches and hammer the recovering database. A dependency outage becomes a total outage. Liveness should test only the process itself.

<a id="dn-124"></a>

#### Graceful shutdown, end to end

```
SIGTERM -> readiness starts failing -> LB stops sending new requests
        -> in-flight requests drain -> background work finishes -> exit
```

```csharp
builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(30));   // default is 5
```

Add `preStop: sleep 5` in the pod spec: endpoint removal and SIGTERM race each other, so without that pause some requests still arrive after the app began shutting down.

<a id="dn-125"></a>

#### Rollout strategies

| Strategy | How | Cost / risk |
|---|---|---|
| **Rolling** | Replace pods gradually | Default; both versions run at once — **schema must be backward compatible** |
| **Blue-green** | Two full environments, flip traffic | Instant rollback; double the infrastructure |
| **Canary** | 5% of traffic to the new version, watch metrics, ramp | Best risk control; needs good telemetry and traffic splitting |
| **Feature flags** | Deploy dark, enable per cohort | Decouples deploy from release; flags become debt if not removed |

Rolling deploys are the reason **expand-and-contract migrations** (Part 14.7) are mandatory rather than optional: for a period, old and new code both run against one schema.

<a id="dn-126"></a>

#### The twelve-factor points that actually bite

- **Config from the environment**, never baked into the image — one artifact promoted across environments.
- **Stateless processes** — no in-memory session, no local file uploads, no per-pod caches you rely on for correctness (Part 6.1).
- **Logs to stdout** as structured JSON; the platform handles shipping. Don't write log files inside a container.
- **Disposability** — fast startup, graceful shutdown, and crash-safe. Assume the pod can vanish at any moment.

---

### Deployment & DevOps

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Deployment & DevOps</p>

<p class="pd">From dotnet publish to a running production system: containers, orchestration, pipelines, release strategies, and the operational habits that keep it up.</p>

<a id="be-a219"></a>

#### CI/CD pipelines

``` sql
name: ci
on:
  push:     { branches: [main] }
  pull_request:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '9.0.x' }
      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build --logger trx --collect:"XPlat Code Coverage"
      - run: dotnet list package --vulnerable --include-transitive
      - name: Build and push image
        if: github.ref == 'refs/heads/main'
        run: |
          dotnet publish src/Orders.Api -c Release /t:PublishContainer \
            -p:ContainerImageTag=${{ github.sha }}
```

1.  **Build once.** One artifact promoted through every environment. Rebuilding per environment means you never tested what you shipped.
2.  **Fail fast.** Compile, unit tests, and static analysis before anything slow.
3.  **Gate on security.** Vulnerable packages and image scans block the merge, not the release.
4.  **Apply migrations as their own stage**, before the rollout, with a reviewed script.
5.  **Deploy automatically to staging, with approval to production** — until your confidence and test suite justify continuous deployment.
6.  **Tag the image with the commit SHA**, so every running pod is traceable to a line of code.

GitOps (Argo CD, Flux) inverts the last step: CI pushes a manifest change to a git repo, and an in-cluster agent reconciles the cluster to match. The cluster state is then always describable, diffable and revertible.

###### A pipeline that is trustworthy, and the concurrency trap

``` text
  the stages, and what each must guarantee

  BUILD      compile, unit test, produce ONE artifact
             fail fast: lint and typecheck before the slow tests
  SCAN       vulnerable dependencies, image scan, secret scan
  PUBLISH    push by DIGEST, with provenance
  DEPLOY DEV automatic. Integration tests run against it.
  DEPLOY TST automatic. End-to-end and contract tests.
  DEPLOY PRD migrations FIRST, as a gated separate step
             then rolling or canary
  VERIFY     smoke tests, then watch error rate and latency;
             automated rollback if they degrade
```

``` text
# THE CONCURRENCY TRAP - two merges racing to deploy
#
#   merge PR1 -> commit A -> run #1 builds A -> deploys A
#   merge PR2 -> commit B -> run #2 builds B -> deploys B
#
# if run #1 is slower (a flaky retry, an agent queue), it can finish AFTER
# run #2 and redeploy the OLDER commit over the newer one. Nothing errors.
# The environment is simply, silently, behind.

# GitHub Actions
concurrency:
  group: deploy-${{ github.ref }}
  cancel-in-progress: false      # QUEUE deploys; never cancel one mid-flight

# Azure DevOps
trigger:
  batch: true                    # collapse pushes during a run into ONE next run
# plus an Exclusive Lock check on the environment
```

| Property                        | Why it matters                                                               |
|---------------------------------|------------------------------------------------------------------------------|
| Under ten minutes to production | Slower, and people batch changes — and big batches are riskier               |
| Deterministic                   | The same commit produces the same artifact. Pin dependencies and base images |
| Idempotent                      | Re-running a deploy is safe                                                  |
| Observable                      | You can see which commit is in which environment, right now                  |
| Reversible                      | Rollback is one action, tested, and needs no rebuild                         |
| Gated on signal, not ceremony   | An approval nobody reads is theatre; automated checks are not                |

On migrations specifically: run them as a **separate, gated step before the application rollout**, never as a startup hook. With three replicas, three instances race; most tools take an advisory lock, so two block for the whole duration and then fail readiness. Combined with expand-and-contract, this ordering is what keeps application rollback possible even though the schema only moves forward.

<a id="be-a220"></a>

#### Deployment strategies

| Strategy     | Mechanism                              | Cost                          | Rollback                    |
|--------------|----------------------------------------|-------------------------------|-----------------------------|
| Recreate     | Stop old, start new                    | Downtime                      | Redeploy old                |
| Rolling      | Replace pods a few at a time           | Both versions live at once    | Roll forward or back        |
| Blue-green   | Two full environments, flip the router | Double infrastructure         | Instant, flip back          |
| Canary       | 1% → 10% → 100% by metrics             | Needs good telemetry          | Stop and shift traffic back |
| Feature flag | Ship dark, enable per cohort           | Flag debt if never cleaned up | Toggle off in seconds       |

Rolling is the Kubernetes default and the right baseline. What it demands of your application is **N-1 compatibility**: version 1.14 and 1.15 must run simultaneously against the same database and the same message schemas, because for a few minutes they will.

``` text
  a safe database change, across three releases

  release 1   add nullable column, write to old AND new, read old
  release 2   backfill; switch reads to new column
  release 3   stop writing old column; drop it

  at every point, both the previous and current app version work
```

> **\[Trap\]**
>
> **Common trap.** Migrations that run on application startup race each other across replicas and cannot be rolled back with the deployment. Run them as a separate pipeline stage or a Kubernetes Job with a single completion.

###### Worked example

The expand-and-contract database migration, written out as three real releases, because ‘make it backwards compatible’ is easy to say and easy to get wrong.

Goal: rename `Orders.Notes` (nvarchar) to `Orders.CustomerNotes`, with zero downtime and three replicas running.

``` csharp
-- RELEASE 1 (expand): add, do not remove. Migration runs BEFORE the app rollout.
ALTER TABLE Orders ADD CustomerNotes nvarchar(max) NULL;

-- app v1.1: writes BOTH columns, reads the OLD one
public string? Notes
{
    get => _notes;
    set { _notes = value; _customerNotes = value; }     // dual write
}
-- during the rollout, v1.0 pods (write Notes only) and v1.1 pods coexist.
-- v1.0 writes are still correct because v1.1 reads Notes.
```

``` sql
-- RELEASE 2 (migrate): backfill, then switch reads.
UPDATE Orders SET CustomerNotes = Notes WHERE CustomerNotes IS NULL;  -- batched!

-- app v1.2: writes BOTH, reads the NEW one
-- v1.1 pods (read old, write both) and v1.2 pods (read new, write both) coexist safely,
-- because both columns are being kept in sync by every writer.
```

``` sql
-- RELEASE 3 (contract): stop writing the old column, then drop it.
-- app v1.3: writes and reads CustomerNotes only.
-- ...deploy completes, all pods on v1.3...
-- THEN, in a later migration:
ALTER TABLE Orders DROP COLUMN Notes;
```

``` text
  the invariant that makes this safe

  at every instant, for any pair of app versions that can coexist:
    every column a running version READS is written by every running version
    every column a running version WRITES exists in the schema

  releases 1 and 3 each violate nothing. Combining them into one release
  (add + backfill + switch + drop) violates it during the rollout window,
  which is exactly the 90 seconds when both versions are live.
```

| Change                               | Safe in one release?                   | Why                                                               |
|--------------------------------------|----------------------------------------|-------------------------------------------------------------------|
| Add a nullable column                | Yes                                    | Old code ignores it                                               |
| Add a NOT NULL column with a default | Usually                                | Old inserts get the default; check lock behaviour on large tables |
| Drop a column                        | No                                     | Old pods still SELECT it → expand/contract                        |
| Rename anything                      | No                                     | It is a drop plus an add                                          |
| Widen a column                       | Yes                                    | Existing values still fit                                         |
| Narrow a column or add a constraint  | No                                     | Old code can write violating data mid-rollout                     |
| Add an index                         | Yes, but use `CONCURRENTLY` / `ONLINE` | Otherwise it locks the table                                      |

###### Canary releases: what to actually watch

A canary that only watches error rate misses most of what goes wrong. The list below is roughly in order of how often each one catches something a smoke test did not.

- **Error rate**, split by status class — a rise in 4xx means a contract change, 5xx means a bug.
- **Latency p50 *and* p99** — a regression often shows only at the tail, where a new N+1 query lives.
- **Business metrics** — orders per minute, successful logins. A deploy that halves conversion while returning 200 for everything is the failure mode monitoring misses.
- **Downstream call volume** — a sudden 5× increase in calls to another service is an N+1 that will page someone else at 3am.
- **Resource use per request** — memory per request climbing means a leak that a five-minute canary will not surface but a two-hour one will.
- **Log error volume**, not just HTTP errors — caught-and-swallowed exceptions never reach the status code.

``` text
# Argo Rollouts: promote automatically only if the analysis passes
strategy:
  canary:
    steps:
      - setWeight: 5
      - pause: { duration: 10m }
      - analysis:
          templates: [{ templateName: success-rate-and-latency }]
      - setWeight: 25
      - pause: { duration: 10m }
      - setWeight: 50
      - pause: { duration: 30m }
      - setWeight: 100
```

The pauses matter more than the weights. A five-percent canary for two minutes proves almost nothing — it will not have hit the slow path, the hourly job, or the memory ceiling. Enough time at each step to see a full traffic cycle is what makes a canary a test rather than a ritual.

### Application Architecture

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Application Architecture</p>

<p class="pd">How a .NET application is wired together before any business logic exists: the project system, the container, configuration, logging, the host, and the architectural patterns that decide where code goes.</p>

<a id="be-a70"></a>

#### Feature flags and decoupling deploy from release

Deploying code and releasing behaviour are separate events. Once they are separate, deployments become boring, which is the entire goal of modern delivery.

``` csharp
builder.Services.AddFeatureManagement();

// declarative
[FeatureGate("NewPricingEngine")]
public class PricingV2Controller : ControllerBase { }

// imperative
if (await _features.IsEnabledAsync("NewPricingEngine"))
    return await _v2.PriceAsync(basket, ct);
return await _v1.PriceAsync(basket, ct);
```

``` json
{
  "FeatureManagement": {
    "NewPricingEngine": {
      "EnabledFor": [
        { "Name": "Percentage", "Parameters": { "Value": 10 } },
        { "Name": "Targeting",  "Parameters": { "Audience": { "Groups": ["beta"] } } }
      ]
    }
  }
}
```

| Flag type                | Lifetime               | Rule                                                        |
|--------------------------|------------------------|-------------------------------------------------------------|
| Release toggle           | Days to weeks          | Delete it the moment the feature is fully on                |
| Experiment               | The length of the test | Has an owner and an end date, or it is not an experiment    |
| Ops kill switch          | Permanent              | Documented in the runbook; tested in staging                |
| Permission / entitlement | Permanent              | Not really a flag — this is authorization, model it as such |

> **\[Trap\]**
>
> **Common trap.** Flag debt compounds: n independent flags means 2ⁿ possible code paths, none of which are tested together. Put an expiry date on every release toggle and fail the build when one is overdue.

###### Flag types, and the debt they create

| Kind                         | Lifespan      | Changed by    | Cleanup                                   |
|------------------------------|---------------|---------------|-------------------------------------------|
| **Release toggle**           | Days to weeks | The team      | **Delete as soon as it is on everywhere** |
| **Experiment**               | Weeks         | Product       | Delete when the experiment concludes      |
| **Ops toggle** / kill switch | Permanent     | On-call       | Keep, and document it                     |
| **Entitlement**              | Permanent     | Configuration | Not a flag — this is authorisation        |

``` csharp
// keep the check at ONE point, as high in the call stack as possible
if (await flags.IsEnabledAsync("new-pricing-engine"))
    return await _newPricing.CalculateAsync(order, ct);
return await _legacyPricing.CalculateAsync(order, ct);

// NOT scattered through the implementation - inside methods, inside loops,
// inside the repository. That produces 2^N paths, two of which are tested.

// with targeting, for a gradual rollout
var enabled = await flags.IsEnabledAsync("new-pricing", new TargetingContext
{
    UserId = user.Id,
    Groups = [user.TenantId],
});
// consistent hashing on UserId, so one user gets the same answer on every
// request - otherwise a page half-renders each variant.
```

``` text
  FLAG DEBT - the failure mode nobody plans for

  toggle added         "we will remove it after the rollout"
  rollout completes    nobody removes it
  six months later     47 flags, all on, nobody knows which are safe to
                       delete, and the code has 2^47 nominal paths

  controls that actually work
    a REMOVAL TICKET created at the same time as the flag, with an owner
    an expiry date IN the flag, and a build warning past it
    a dashboard of flag age, reviewed monthly
    a hard cap on open release toggles

  and the test discipline: test the branch you intend to ship BOTH ways.
  testing only one is how a fully-rolled-out flag turns out never to have
  worked.
```

The payoff is worth restating: with flags, *deploying* becomes a low-risk routine event and *releasing* becomes a decision reversible in seconds with no pipeline run. That separation does more for deployment confidence than any rollout strategy — a canary still needs a deploy to undo; a flag does not.

### Observability, testing, and delivery

<p class="src">From <em>Backend Engineering</em> · Data, Collections, LINQ & SQL · Observability, testing, and delivery</p>

<a id="be-m273"></a>

#### Feature flags and progressive delivery

A flag decouples *deploying* code from *releasing* behaviour. That separation is what makes small, frequent, low-risk deployments possible.

``` csharp
builder.Services.AddFeatureManagement();

if (await _flags.IsEnabledAsync("keyset-pagination"))
    return await GetPageKeyset(cursor, ct);
return await GetPageOffset(page, ct);            // (§296)
```

| Flag kind       | Lifetime                        | Risk                                    |
|-----------------|---------------------------------|-----------------------------------------|
| Release toggle  | days — **delete after rollout** | accumulates as dead branches            |
| Experiment      | the length of the experiment    | results need a real analysis            |
| Ops kill switch | permanent                       | must be tested, or it fails when needed |
| Entitlement     | permanent                       | this is a product feature, not a flag   |

``` text
// Progressive rollout, in order of increasing confidence:
//   1. off in production, on for the team          (dark)
//   2. 1% of tenants, watching error rate and p99
//   3. 10%, 50%, 100%
//   4. DELETE the flag and the old branch
// Step 4 is the one that gets skipped, and skipping it is how a codebase
// accumulates dozens of dead paths nobody dares remove.
```

> **\[Tip\]**
>
> **Flags pair with shadow comparison for risky changes.** Run both the old and new implementation, return the old result, and log any mismatch. That gives you production-traffic evidence before switching — far stronger than any test suite for a change like a rewritten pricing calculation ([Case: the report whose totals were wrong](14-03-worked-cases-from-production.md#be-m295), [The migration path, in order](12-01-monoliths-modules-and-services.md#be-m252)).

> **\[Trap\]**
>
> **Flags multiply combinatorially.** Ten independent flags is 1,024 possible configurations, and you have tested a handful. Keep the number of simultaneously-live release toggles small, set an expiry date when you create one, and treat a flag older than its expiry as a bug to fix rather than a setting to keep.

### Deployment

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Deployment</p>

<p class="pd">Getting code from a commit to a running production system, repeatably, without downtime, and with a way back when it goes wrong.</p>

<a id="be-g700"></a>

#### Build once, deploy everywhere

``` text
  the rule that underpins everything else

  WRONG                              RIGHT
  build for dev   -> deploy dev      build ONCE -> artifact
  build for test  -> deploy test                    |
  build for prod  -> deploy prod                    +-> deploy to dev
                                                    +-> deploy to test
  three different binaries.                         +-> deploy to prod
  you tested something OTHER than
  what you shipped.                  one binary, promoted through environments.
                                     what you tested IS what runs.

  which means CONFIGURATION MUST BE EXTERNAL to the artifact.
  no connection strings compiled in, no per-environment builds,
  no #if PRODUCTION.
```

| Artifact        | Immutable identity                                |
|-----------------|---------------------------------------------------|
| Container image | Digest — `sha256:abc...`, not a mutable tag       |
| Package         | Version number, published once, never republished |
| Zip / tarball   | Content hash, stored in an artifact repository    |
| Machine image   | Image id, built by Packer                         |

``` yaml
# tag by digest in production, not by name.
# 'myapp:latest' or even 'myapp:v2.1' can be re-pushed and now means
# something different. A digest cannot.
image: registry.example.com/myapp@sha256:9f3c2a1b...

# and record what a build IS, so a running container can be traced to a commit
LABEL org.opencontainers.image.revision="$GIT_SHA"
LABEL org.opencontainers.image.created="$BUILD_DATE"
LABEL org.opencontainers.image.source="https://github.com/acme/myapp"

# expose it at runtime too
ENV BUILD_SHA=$GIT_SHA
# /health returns it, so "which version is actually running?" is answerable
# in five seconds during an incident.
```

> **\[Why it matters\]** Rebuilding per environment is how “it passed in staging” stops meaning anything: the artefact that was tested is not the artefact that shipped. One immutable build promoted through environments makes a rollback a matter of pointing at the previous digest, and makes the question “what is actually running in production?” answerable with certainty.

<a id="be-g701"></a>

#### Configuration and secrets

| Kind                    | Example                              | Where it belongs                                           |
|-------------------------|--------------------------------------|------------------------------------------------------------|
| **Build-time constant** | Feature that does not vary           | In the code                                                |
| **Environment config**  | Log level, timeouts, feature toggles | Environment variables or a config service                  |
| **Connection details**  | Hostnames, ports, queue names        | Environment config                                         |
| **Secrets**             | Passwords, API keys, certificates    | **A secret manager**. Never in the repository or the image |
| **Runtime-changeable**  | Feature flags, rate limits           | A flag service, changeable without deploying               |

``` text
# validate configuration at STARTUP and fail fast. A service that boots with
# a missing connection string and fails on the first request is worse than
# one that refuses to start - the second is caught by the deployment, the
# first is caught by a user.

# secrets: injected at runtime, never baked in
#   Kubernetes: a Secret mounted as a file (better than env - env leaks into
#               crash dumps, child processes and error reports)
#   Cloud:      Secrets Manager / Key Vault, fetched with a workload identity
#   Best:       NO long-lived secret at all - workload identity federation,
#               where the platform vouches for the pod and the database
#               accepts a short-lived token
```

- **Prefer identity to secrets.** Managed identity, IRSA, workload identity federation — a credential that does not exist cannot leak or need rotating.
- **Mount secrets as files, not environment variables.** Environment variables appear in crash dumps, process listings, child processes, and frequently in error-tracking payloads.
- **Rotate on a schedule, and prove rotation works** before you need it in an emergency.
- **Scan the repository history**, not just the current tree — a secret committed and removed is still in the history and must be revoked, not just deleted.
- **Separate secrets per environment.** A development credential must not work against production.
- **Audit access to the secret store.** Who read what, and when.

###### Why mounted files beat environment variables for secrets

``` text
  an ENVIRONMENT VARIABLE holding a secret is readable from far more
  places than people expect:

    /proc/<pid>/environ           any process running as the same user
    `ps eww <pid>`                on some systems
    every CHILD PROCESS inherits it - including a shell you exec for
      diagnostics, and any subprocess it spawns
    CRASH DUMPS contain the whole environment block
    error-tracking SDKs frequently attach environment context
    `docker inspect`, `kubectl describe pod`
    the Aspire dashboard, and most orchestrator UIs
    CI logs, whenever something echoes the environment

  and it CANNOT BE ROTATED without restarting the process.

  a MOUNTED FILE:
    readable only by the process, with filesystem permissions
    not inherited by children
    not in crash dumps or `describe` output
    CAN BE ROTATED IN PLACE - the file changes, and a watcher reloads
```

``` text
# mount secrets as files
volumeMounts:
  - { name: secrets, mountPath: /run/secrets, readOnly: true }
volumes:
  - name: secrets
    secret: { secretName: orders-secrets, defaultMode: 0400 }
```

``` csharp
// and read them with the key-per-file provider - one file per setting
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true,
                                    reloadOnChange: true);
//                                                  ^^^^^^^^^^^^^^^^ rotation
// with reloadOnChange, an IOptionsMonitor<T> consumer picks up a rotated
// secret WITHOUT A RESTART. IOptions<T> will not - it reads once, ever.

builder.Services.AddOptions<DbOptions>()
    .Bind(builder.Configuration.GetSection("Db"))
    .ValidateOnStart();          // refuse to BOOT on bad config, rather
                                 // than failing on the first request
                                 // that happens to need it
```

| Approach                          | Rotation     | Exposure                            | Verdict                            |
|-----------------------------------|--------------|-------------------------------------|------------------------------------|
| Committed to the repository       | —            | **Total, and permanent in history** | Never                              |
| Environment variable              | Restart      | High — see above                    | Acceptable for non-secrets         |
| Mounted file                      | **In place** | Low                                 | Good                               |
| Secret store + SDK                | In place     | Low                                 | Good; adds a runtime dependency    |
| **CSI driver / External Secrets** | **In place** | **Lowest**                          | **Best — no stored secret at all** |
| Workload identity, no secret      | N/A          | None                                | The destination                    |

The destination is worth naming explicitly: with workload identity, the application authenticates to the database or the secret store *as itself*, using a short-lived token the platform issues. There is no secret to store, rotate, leak or audit — and every access is attributable to a specific workload in the provider's audit log.

<a id="be-g702"></a>

#### Deployment strategies

| Strategy         | How                                       | Downtime | Rollback                             | Cost             |
|------------------|-------------------------------------------|----------|--------------------------------------|------------------|
| **Recreate**     | Stop all, start all                       | **Yes**  | Redeploy previous                    | Lowest           |
| **Rolling**      | Replace instances gradually               | No       | Roll forward, or roll back gradually | Low              |
| **Blue-green**   | Two full environments, switch traffic     | No       | **Instant** — switch back            | 2× during deploy |
| **Canary**       | Small percentage first, then increase     | No       | Fast — shift traffic back            | Low              |
| **Shadow**       | Send a copy of traffic; discard responses | No       | N/A — not serving                    | Extra capacity   |
| **Feature flag** | Deploy dark, enable later                 | No       | **Instant, no deploy**               | Flag complexity  |

``` text
  ROLLING is the default, and it has one hard requirement:

    during the rollout, VERSION N AND N+1 RUN SIMULTANEOUSLY.

  so every change must be backward compatible with the version it replaces:
    - the API must accept requests from old clients
    - the database schema must work for BOTH versions
    - message formats must be readable by both
    - shared caches must not be poisoned by a new format

  this is why EXPAND AND CONTRACT exists, and why it is not optional
  for zero-downtime deployment.
```

``` yaml
# canary with a service mesh or a smart ingress
apiVersion: split.smi-spec.io/v1alpha2
kind: TrafficSplit
spec:
  service: api
  backends:
    - service: api-stable
      weight: 95
    - service: api-canary
      weight: 5

# and the crucial part: AUTOMATED analysis, not a human watching a dashboard.
# Flagger, Argo Rollouts: compare canary error rate and latency against
# stable, and roll back automatically if the canary is worse.
# A canary nobody is watching is just a slower deploy.
```

> **\[Note\]**
>
> **Separate deploying from releasing.** Deploying puts code on servers. Releasing exposes behaviour to users. Feature flags make them independent, which means a deploy becomes a low-risk routine event and a release becomes a decision you can reverse in seconds without a pipeline run. This single separation does more for deployment confidence than any strategy in the table above.

###### Canary analysis, and what to measure before promoting

``` text
  A CANARY IS NOT "DEPLOY TO ONE INSTANCE AND LOOK AT IT"

  it is a comparison against a control, over a defined window, on
  metrics agreed IN ADVANCE.

    baseline: the current version, taking 95% of traffic
    canary:   the new version, taking 5%

    both measured over the SAME window, on the SAME metrics:
      error rate            canary must not exceed baseline x 1.1
      p99 latency           canary must not exceed baseline x 1.2
      CPU and memory        canary must not exceed baseline x 1.3
      business metric       orders/min must not DROP

  the business metric is the one that catches what technical metrics
  miss: a version returning 200 OK with an empty result set has a
  perfect error rate and a perfect latency profile, and has broken
  checkout entirely.

  MINIMUM WINDOW: long enough to see a full traffic cycle and at
  least one GC and one cache expiry. Ten minutes is usually the floor;
  "it looked fine for 30 seconds" is not a canary.
```

| Strategy         | Rollback                          | Cost                                 | Fits                                    |
|------------------|-----------------------------------|--------------------------------------|-----------------------------------------|
| **Recreate**     | Redeploy the old version          | Downtime                             | Dev only                                |
| **Rolling**      | Roll forward, or roll back slowly | Two versions coexist                 | The default                             |
| **Blue/green**   | **Switch traffic back — seconds** | Double infrastructure                | When rollback speed matters most        |
| **Canary**       | Shift traffic back                | Needs traffic splitting and analysis | High-risk changes                       |
| **Feature flag** | **Toggle — no deploy at all**     | Flag debt                            | **The most reversible option there is** |

> **\[Trap\]**
>
> **Common trap.** Rolling and blue/green both require the two versions to be *compatible*, because both run simultaneously. That includes the database: a migration that drops a column breaks the old version still serving traffic. Use expand-and-contract — add the new column, deploy code writing both, backfill, deploy code reading the new one, and only drop the old column a release later. Skipping this is why “we can roll back” is often untrue in practice.

<a id="be-g703"></a>

#### Zero-downtime: what it actually requires

1.  **Health checks the platform trusts.** Readiness must be accurate, or traffic goes to an instance that is not ready.
2.  **Graceful shutdown.** Handle `SIGTERM`: stop accepting, drain in-flight, close cleanly, exit 0.
3.  **Connection draining at the load balancer.** Stop sending new requests before the instance stops.
4.  **A drain delay.** Endpoint removal takes seconds to propagate — keep serving during that window.
5.  **Backward-compatible schema changes.** Expand and contract; never a breaking change in one release.
6.  **Backward-compatible contracts.** Old clients keep working, because during the rollout they exist.
7.  **Idempotent, resumable migrations**, applied once, before the application rollout.

``` csharp
// the shutdown sequence, in any language. This is the part most services skip.
async Task Shutdown() {
    // 1. FAIL READINESS FIRST, and keep serving while it propagates
    _ready = false;
    await Task.Delay(TimeSpan.FromSeconds(10));

    // 2. stop accepting new work, drain what is in flight
    await _server.StopAsync(TimeSpan.FromSeconds(30));

    // 3. close dependencies in order
    await _queueConsumer.StopAsync();
    await _dbPool.DisposeAsync();
}
// and the platform's grace period must EXCEED 10 + 30 seconds,
// or you are SIGKILLed mid-drain and the whole exercise is pointless.
```

``` sql
-- expand and contract, as three releases. Renaming a column, with zero downtime:

-- RELEASE 1 (expand)
ALTER TABLE orders ADD COLUMN customer_notes text;
-- app writes BOTH columns, reads the OLD one.
-- old version (writes notes only) and new coexist safely.

-- RELEASE 2 (migrate)
UPDATE orders SET customer_notes = notes WHERE customer_notes IS NULL;  -- batched
-- app writes both, reads the NEW one.

-- RELEASE 3 (contract) - after the rollout completes
ALTER TABLE orders DROP COLUMN notes;

-- the invariant: at every instant, every column a running version READS
-- is written by every running version. Combining the three violates it
-- during the rollout window - which is exactly the 90 seconds that matter.
```

| Change                | Safe in one release?                                   |
|-----------------------|--------------------------------------------------------|
| Add a nullable column | Yes                                                    |
| Add an index          | Yes — with `CONCURRENTLY` / `ONLINE`                   |
| Widen a column        | Yes                                                    |
| Drop a column         | **No** — old code still selects it                     |
| Rename anything       | **No** — it is a drop plus an add                      |
| Add a constraint      | **No** — old code can write violating data mid-rollout |
| Change a column type  | **No**, and it usually rewrites the table under a lock |

> **\[Trap\]**
>
> **Common trap.** Always set `lock_timeout` before running DDL. Without it, a migration that cannot acquire its lock *queues* — and every subsequent query on that table queues behind it. One long report plus one unbounded `ALTER TABLE` is a complete outage on a healthy database.

###### Expand and contract, in full, with the timeline

``` text
  RENAMING A COLUMN, with zero downtime. Four deploys, and you cannot
  skip any of them - because during every rollout BOTH VERSIONS RUN.

  DEPLOY 1 - EXPAND (schema only, no code change)
      ALTER TABLE orders ADD COLUMN total_amount numeric;
      -- nullable, no default, no rewrite. Instant on any size table.
      old code ignores it. Safe to roll back.

  DEPLOY 2 - WRITE BOTH
      code writes BOTH `total` and `total_amount`, reads `total`.
      now every new row has both. Safe to roll back to deploy 1.

  BACKFILL (not a deploy - a batched job)
      UPDATE orders SET total_amount = total
      WHERE total_amount IS NULL AND id BETWEEN :lo AND :hi;
      -- 10,000 rows at a time, with a commit between. A single
      -- statement over 50M rows locks the table for hours and its
      -- ROLLBACK takes just as long.

  DEPLOY 3 - READ NEW
      code writes both, reads `total_amount`.
      the risky one - verify the backfill is complete FIRST:
        SELECT count(*) FROM orders WHERE total_amount IS NULL;  -- must be 0
      still safe to roll back: deploy 2 read the old column, which is
      still being written.

  DEPLOY 4 - CONTRACT
      code writes and reads only `total_amount`.
      ONE RELEASE LATER:  ALTER TABLE orders DROP COLUMN total;
      -- do NOT do this in the same release. If you must roll back to
      -- deploy 3, that code still writes `total`.

  the whole point: at every instant, the version running BEFORE and the
  version running AFTER both work against the CURRENT schema.
```

| Migration                   | Safe online?                  | Note                                            |
|-----------------------------|-------------------------------|-------------------------------------------------|
| Add a nullable column       | **Yes**                       | Instant                                         |
| Add a column with a default | Postgres 11+ yes              | Older versions rewrite the whole table          |
| Add an index                | With `CONCURRENTLY`           | Slower; cannot run inside a transaction         |
| Drop a column               | Yes, *after* no code reads it | One release later                               |
| Rename anything             | **Never directly**            | Expand and contract                             |
| Change a type               | Rarely                        | New column, backfill, swap                      |
| Add `NOT NULL`              | Two steps                     | Add a `CHECK ... NOT VALID`, validate, then set |
| Add a foreign key           | Two steps                     | `NOT VALID`, then `VALIDATE CONSTRAINT`         |

> **\[Trap\]**
>
> **Common trap.** The migration must run as a **separate, gated step before the application rollout** — never as a startup hook. With three replicas, three instances race to migrate; most tools take an advisory lock, so two block for the entire migration and then fail their readiness probe, turning a deploy into an outage. And because the schema only moves forward while code can roll back, expand-and-contract is what makes rollback possible at all.

<a id="be-g704"></a>

#### Pipelines, rollback and what good looks like

``` text
  a pipeline that is actually trustworthy

  commit
    |
  BUILD          compile, run unit tests, produce ONE artifact
    |            fail fast: lint and typecheck before the slow tests
  SCAN           dependency vulnerabilities, image scan, secret scan
    |
  PUBLISH        push the artifact, tagged by digest, with provenance
    |
  DEPLOY DEV     automatic. Run integration tests against it.
    |
  DEPLOY TEST    automatic. Run end-to-end and contract tests.
    |
  DEPLOY PROD    gated: approval, or fully automatic if you trust the above
    |            migrations run FIRST, as a separate step
    |            then a rolling or canary rollout
  VERIFY         smoke tests, then watch error rate and latency
    |            automated rollback if they degrade
```

| Property                      | Why it matters                                                                        |
|-------------------------------|---------------------------------------------------------------------------------------|
| Fast                          | Under 10 minutes to production, or people batch changes — and big batches are riskier |
| Deterministic                 | The same commit produces the same artifact. Pin dependencies and base images          |
| Idempotent                    | Re-running a deploy is safe                                                           |
| Observable                    | You can see which commit is in which environment, right now                           |
| Reversible                    | Rollback is one action, tested, and does not require a rebuild                        |
| Gated on signal, not ceremony | An approval step that nobody reads is theatre; automated checks are not               |

``` text
# concurrency control, so two merges do not race to deploy
# GitHub Actions
concurrency:
  group: deploy-${{ github.ref }}
  cancel-in-progress: false     # QUEUE deploys - never cancel one mid-flight

# Azure DevOps
trigger:
  batch: true                   # collapse pushes during a run into one next run
# plus an Exclusive Lock check on the environment

# without this, run #1 (older commit) can finish AFTER run #2 and
# redeploy stale code over the newer version, silently.
```

| Rollback approach                   | When it works                                                |
|-------------------------------------|--------------------------------------------------------------|
| Redeploy the previous artifact      | The default. Requires no forward-only schema change was made |
| Traffic shift (blue-green / canary) | Fastest. The old version is still running                    |
| Feature flag off                    | Fastest of all — no deployment at all                        |
| Roll forward with a fix             | When the schema changed and rollback is not possible         |
| Restore from backup                 | Last resort. Data loss between the backup and now            |

> **\[Note\]**
>
> **Migrations are what make rollback hard.** Code rolls back trivially; a schema change frequently does not. This is the real argument for expand-and-contract: because every intermediate state is compatible with both versions, you can always roll the *application* back even though the schema only moves forward. Design for that, and rollback stops being a gamble.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a212"></a>

#### Ephemeral environments and golden paths

Two platform-engineering ideas that pay for themselves quickly in a multi-service .NET estate.

##### Ephemeral environments

A full, isolated deployment per pull request, created on open and destroyed on merge. It turns “can you review my branch?” into a URL, and it lets integration tests run against a real deployment rather than a shared staging environment that three teams are simultaneously breaking.

``` bash
name: pr-environment
on:
  pull_request: { types: [opened, synchronize, closed] }

jobs:
  deploy:
    if: github.event.action != 'closed'
    steps:
      - uses: actions/checkout@v4
      - name: Build and push
        run: |
          dotnet publish src/Orders.Api -c Release /t:PublishContainer \
            -p:ContainerImageTag=pr-${{ github.event.number }}
      - name: Deploy namespace
        run: |
          helm upgrade --install orders-pr-${{ github.event.number }} ./charts/orders \
            --namespace pr-${{ github.event.number }} --create-namespace \
            --set image.tag=pr-${{ github.event.number }} \
            --set ingress.host=pr-${{ github.event.number }}.preview.internal \
            --values ./charts/orders/values-ephemeral.yaml
      - name: Comment the URL
        run: gh pr comment ${{ github.event.number }} \
               --body "Preview: https://pr-${{ github.event.number }}.preview.internal"

  teardown:
    if: github.event.action == 'closed'
    steps:
      - run: kubectl delete namespace pr-${{ github.event.number }} --ignore-not-found
```

| Requirement                              | Why                                                                                       |
|------------------------------------------|-------------------------------------------------------------------------------------------|
| A cheap, seeded database per environment | A container plus a seed script; never a copy of production                                |
| Aggressive TTL cleanup                   | A nightly job deleting namespaces older than N days, because teardown will sometimes fail |
| Tiny resource requests                   | Twenty preview environments must not cost more than production                            |
| Stubbed third parties                    | You cannot create twenty sandbox accounts at a payment provider                           |
| Wildcard DNS and certificate             | `*.preview.internal`, or every environment needs manual DNS                               |

##### Golden paths

A golden path is the supported, documented, boring way to do a common thing — and crucially, a template that generates it. The alternative is fifteen services with fifteen opinions about health endpoint paths, log formats, retry policies and pipeline structure, which is what makes an estate expensive to operate.

``` bash
# a service template, so a new service starts correct
dotnet new install Acme.Templates
dotnet new acme-service --name Invoicing --with-database postgres --with-broker rabbitmq

# what it generates:
#   SDK-style projects, central package management, .editorconfig, analyzers as errors
#   ServiceDefaults wired: OpenTelemetry, health checks, resilient HTTP clients
#   Dockerfile with correct layer caching and a chiseled base
#   Helm chart with probes, resource requests, PDB, topology spread
#   CI pipeline: build, test, vulnerability gate, image scan, sign, deploy
#   ADR folder with the first decision record already written
```

> **\[Note\]**
>
> **Golden, not mandatory.** A path that teams cannot deviate from becomes a bottleneck and gets circumvented. The contract worth enforcing is the *interface* — expose these health endpoints, emit these telemetry attributes, publish an OpenAPI document, respond to SIGTERM — not the implementation. Make the supported path so much easier than the alternatives that deviating is a deliberate, justified choice.

###### What makes an ephemeral environment feasible

``` text
  the goal: every pull request gets a running environment, torn down on merge.

  what makes it POSSIBLE
    infrastructure as code       the environment is a function of a commit
    containerised services       no machine-specific setup
    seeded, small datasets       not a copy of production
    external dependencies faked  MSW, WireMock, or a shared sandbox
    automated DNS and TLS        wildcard cert, per-PR subdomain
    a hard TTL                   destroyed automatically, always

  WHAT USUALLY BLOCKS IT
    the DATABASE. A 400 GB production copy cannot be provisioned per PR.
      -> a seeded subset, restored from a template
      -> or database BRANCHING (Neon, PlanetScale) - copy-on-write,
         production-shaped data, created in seconds
    licensing per environment
    a third-party integration with no sandbox
    manual approval steps in the provisioning path
```

``` bash
# a per-PR environment, torn down automatically
- name: Deploy preview
  run: |
    az containerapp up --name "pr-${{ github.event.number }}" \
      --image "$REGISTRY/api@${{ steps.build.outputs.digest }}" \
      --environment preview --ingress external
- name: Comment the URL on the PR
  run: gh pr comment ${{ github.event.number }} --body "Preview: $URL"

- name: Destroy
  if: github.event.action == 'closed'
  run: az containerapp delete --name "pr-${{ github.event.number }}" --yes
# the `if: closed` teardown is the part people forget, and the reason
# preview environments quietly become a large monthly bill.
```

A **golden path** is the other half: a templated, opinionated, fully wired way to create a new service — repository, pipeline, observability, health checks, secrets, dashboard, on-call rota — in one command. The point is not to forbid deviation but to make the well-supported route so much easier than the alternative that most teams take it, and the platform team then supports one shape rather than fifteen.

``` bash
# a golden path, concretely: a dotnet template your platform team owns
dotnet new install Acme.Templates
dotnet new acme-service --name Orders.Api --database postgres --queue servicebus
# generates: the project, ServiceDefaults, Dockerfile, Terraform module,
#            pipeline YAML, health checks, OTel wiring, a README and
#            a starter runbook.
```

### Shipping it

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Shipping it</p>

<p class="pd">The artefacts that turn working code into a running service: the container, the health endpoints, the observability wiring and the deployment manifest.</p>

<a id="be-w15"></a>

#### Health, readiness and graceful shutdown

``` csharp
builder.Services.AddHealthChecks()
    .AddNpgSql(cs, name: "db", tags: ["ready"])
    .AddRedis(redisCs, name: "cache", tags: ["ready"],
              failureStatus: HealthStatus.Degraded)      // cache down ≠ unhealthy
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

// liveness: am I alive? Deliberately checks NOTHING external.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("live")
});

// readiness: can I serve traffic right now?
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = c => c.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// drain: fail readiness the moment SIGTERM arrives, so the load balancer
// stops sending work BEFORE the process starts shutting down
var draining = false;
app.Lifetime.ApplicationStopping.Register(() => draining = true);
app.MapGet("/health/ready", () => draining
    ? Results.StatusCode(503) : Results.Ok());

builder.Services.Configure<HostOptions>(o =>
    o.ShutdownTimeout = TimeSpan.FromSeconds(25));        // < K8s grace period
```

> **\[Trap\]**
>
> **Common trap.** A liveness probe that checks the database is a self-inflicted outage: the database blips, every pod reports unhealthy, Kubernetes restarts all of them at once, and now a recovering database faces a thundering herd of cold instances. Liveness answers “is this process wedged?” and nothing else.
