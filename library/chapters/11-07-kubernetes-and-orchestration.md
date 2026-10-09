---
id: "11-7"
number: "11.7"
title: "Kubernetes and orchestration"
part: 11
part_title: "Shipping software"
words: 6451
minutes: 28
---

# 11.7 Kubernetes and orchestration

> **Quick look.** Kubernetes: the objects you actually use, and whether you need it at all.
>
> - Ask first whether you need it; simpler platforms are often better.
> - Always set resource requests; set memory limits; be careful with CPU limits, which throttle.
> - Startup, readiness and liveness probes answer different questions; liveness must not check dependencies.
> - Grace period must exceed the app's shutdown timeout; add a PodDisruptionBudget and maxUnavailable: 0.
> - An empty Endpoints list usually means no pod passes readiness, not a network problem.
> - Template config with Helm or Kustomize; adopt GitOps; scale queue workers with KEDA, not CPU.


### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A queue worker that never scaled

During a sale, 200,000 messages queue up but the HPA doesn't add pods: workers wait on I/O and CPU stays at 10%.

*Scale on queue length with KEDA*

```yaml
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata: { name: email-worker }
spec:
  scaleTargetRef: { name: email-worker }
  minReplicaCount: 1
  maxReplicaCount: 30
  triggers:
    - type: azure-servicebus
      metadata: { queueName: emails, messageCount: "500" }   # one pod per 500 waiting messages
      authenticationRef: { name: servicebus-auth }
```

> **Lesson:** Scale consumers on the backlog, not CPU. I/O-bound workers look idle while the queue grows.

#### Example 2: A node drain that took every replica down

Routine node maintenance evicted all three replicas at once because they ran on the same node.

*Spread and protect*

```yaml
# Deployment spec
topologySpreadConstraints:
  - maxSkew: 1
    topologyKey: kubernetes.io/hostname
    whenUnsatisfiable: ScheduleAnyway
    labelSelector: { matchLabels: { app: bookings } }
---
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata: { name: bookings }
spec:
  minAvailable: 2
  selector: { matchLabels: { app: bookings } }
```

> **Lesson:** Spread replicas across nodes and set a PodDisruptionBudget, so maintenance never takes all of them.

### Containers & Kubernetes for .NET

<p class="src">From <em>.NET Backend Deep Dive</em> · Containers & Kubernetes for .NET</p>

<a id="dn-156"></a>

#### The rest of the manifest that matters

```yaml
livenessProbe:  { httpGet: { path: /health/live,  port: 8080 }, periodSeconds: 10 }
readinessProbe: { httpGet: { path: /health/ready, port: 8080 }, periodSeconds: 5 }
startupProbe:   { httpGet: { path: /health/live,  port: 8080 }, failureThreshold: 30 }
lifecycle:
  preStop: { exec: { command: ["sleep", "5"] } }   # let the LB drain first
terminationGracePeriodSeconds: 45                  # > HostOptions.ShutdownTimeout
```

Liveness must **not** touch the database (Part 27.1). The startup probe exists so a slow boot doesn't get killed by liveness. The grace period must exceed your shutdown timeout, or SIGKILL arrives mid-drain.

Add a `PodDisruptionBudget` so a node drain doesn't take every replica at once, and prefer **rolling with `maxUnavailable: 0`** so capacity never dips during deploys.

---

### Deployment & DevOps

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Deployment & DevOps</p>

<p class="pd">From dotnet publish to a running production system: containers, orchestration, pipelines, release strategies, and the operational habits that keep it up.</p>

<a id="be-a217"></a>

#### Kubernetes: the objects you actually use

| Object                  | Purpose                                                           |
|-------------------------|-------------------------------------------------------------------|
| Pod                     | One or more containers scheduled together; the unit of scheduling |
| Deployment              | Declares N replicas of a pod template and manages rolling updates |
| Service                 | Stable virtual IP and DNS name in front of matching pods          |
| Ingress / Gateway       | External HTTP routing and TLS termination                         |
| ConfigMap / Secret      | Non-sensitive and sensitive config, mounted or as env vars        |
| HorizontalPodAutoscaler | Scale replicas on CPU, memory or custom metrics                   |
| Job / CronJob           | Run to completion, once or on a schedule                          |
| PodDisruptionBudget     | “Never take more than one replica down voluntarily”               |

``` text
apiVersion: apps/v1
kind: Deployment
metadata: { name: orders-api }
spec:
  replicas: 3
  selector: { matchLabels: { app: orders-api } }
  template:
    metadata: { labels: { app: orders-api } }
    spec:
      containers:
        - name: api
          image: registry.example.com/orders-api:1.14.2
          ports: [{ containerPort: 8080 }]
          env:
            - name: ConnectionStrings__Default
              valueFrom: { secretKeyRef: { name: orders-db, key: connstring } }
          resources:
            requests: { cpu: "100m", memory: "256Mi" }
            limits:   { memory: "512Mi" }
          livenessProbe:
            httpGet:  { path: /health/live,  port: 8080 }
            periodSeconds: 10
          readinessProbe:
            httpGet:  { path: /health/ready, port: 8080 }
            periodSeconds: 5
          startupProbe:
            httpGet:  { path: /health/live,  port: 8080 }
            failureThreshold: 30
```

- **Always set requests.** The scheduler uses them; without them your pod lands anywhere and gets throttled.
- **Set a memory limit, be careful with CPU limits.** CPU limits cause throttling that looks exactly like a slow application.
- **Use a startup probe** for apps with slow warm-up, so liveness does not kill them during boot.
- **Handle SIGTERM.** Kubernetes sends it, waits `terminationGracePeriodSeconds`, then SIGKILLs. Finish in-flight requests and stop consuming from the queue.

###### Worked example

Probe and resource configuration is where most .NET-on-Kubernetes pain comes from. Here is what each setting does to a real deployment, and the failure it prevents.

``` text
spec:
  replicas: 3
  strategy:
    rollingUpdate: { maxSurge: 1, maxUnavailable: 0 }   # never drop below 3 healthy
  template:
    spec:
      terminationGracePeriodSeconds: 60                 # must exceed your longest request
      containers:
        - name: api
          resources:
            requests: { cpu: "200m", memory: "384Mi" }  # what the scheduler reserves
            limits:   { memory: "768Mi" }               # OOM-kill above this. NO cpu limit.
          env:
            - name: DOTNET_gcServer
              value: "1"
            - name: DOTNET_GCHeapHardLimitPercent
              value: "50"                               # hex-free form; keeps GC under the limit
          startupProbe:                                 # protects a slow boot
            httpGet: { path: /health/live, port: 8080 }
            failureThreshold: 30
            periodSeconds: 2                            # allows up to 60 s to start
          livenessProbe:                                # "is this process wedged?"
            httpGet: { path: /health/live, port: 8080 }
            periodSeconds: 10
            failureThreshold: 3
          readinessProbe:                               # "can I serve traffic right now?"
            httpGet: { path: /health/ready, port: 8080 }
            periodSeconds: 5
            failureThreshold: 2
          lifecycle:
            preStop:
              exec: { command: ["sleep", "5"] }         # let endpoints propagate before shutdown
```

| Setting                         | Failure it prevents                                                                 |
|---------------------------------|-------------------------------------------------------------------------------------|
| `maxUnavailable: 0`             | Capacity dip during a rollout causing latency spikes                                |
| No CPU limit                    | CFS throttling — the pod is paused mid-request and p99 explodes while CPU shows 30% |
| `startupProbe`                  | Liveness killing a pod that is still doing EF model build and JIT                   |
| Separate live/ready endpoints   | A database outage restarting every pod in a crash loop                              |
| `preStop` sleep                 | Requests routed to a pod that has already begun shutting down                       |
| `terminationGracePeriodSeconds` | SIGKILL mid-transaction on a long request                                           |

The CPU limit point deserves emphasis because it is counter-intuitive and extremely common. A CPU limit is enforced by the Linux CFS quota: the container gets a slice of each 100 ms period and is then *frozen*. A multi-threaded .NET app with server GC can burn its quota in 20 ms and sit idle for 80, producing latency that looks exactly like a network problem. Set requests for scheduling, set a memory limit for safety, and leave CPU unlimited unless you have a specific noisy-neighbour reason.

###### Graceful shutdown, in full

``` text
  what actually happens on a rolling update, in order

  1  pod marked Terminating; removed from Service endpoints ... EVENTUALLY.
     endpoint propagation to every kube-proxy/ingress is ASYNCHRONOUS and takes
     a few hundred ms to a few seconds. Traffic still arrives during this window.
  2  preStop hook runs      <- this is why a 5 s sleep here matters
  3  SIGTERM sent to PID 1
  4  .NET IHostApplicationLifetime.ApplicationStopping fires
     - Kestrel stops accepting NEW connections, finishes in-flight requests
     - your BackgroundServices see the stoppingToken cancel
  5  host waits up to ShutdownTimeout (default 30 s)
  6  SIGKILL at terminationGracePeriodSeconds. Anything unfinished is lost.
```

``` csharp
builder.Services.Configure<HostOptions>(o =>
{
    o.ShutdownTimeout = TimeSpan.FromSeconds(45);      // < terminationGracePeriodSeconds
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

// a consumer that drains cleanly instead of dying mid-message
protected override async Task ExecuteAsync(CancellationToken stopping)
{
    await foreach (var msg in _consumer.ConsumeAsync(stopping).WithCancellation(CancellationToken.None))
    {
        await HandleAsync(msg, CancellationToken.None);   // finish THIS message
        await _consumer.AckAsync(msg);
        if (stopping.IsCancellationRequested) break;      // then stop, between messages
    }
}
```

Note the deliberate use of `CancellationToken.None` inside the loop: passing the stopping token to the handler would abort a message halfway through, which is exactly the outcome graceful shutdown exists to prevent. You want to stop taking new work, not abandon work in progress.

<a id="be-a218"></a>

#### Configuration, secrets and Helm

Three environments times twenty services is sixty near-identical YAML files. Helm templates them; Kustomize patches a base with overlays. Either is fine — pick one and be consistent.

``` text
# values-prod.yaml
replicaCount: 6
image: { repository: registry.example.com/orders-api, tag: "1.14.2" }
resources: { requests: { cpu: 200m, memory: 512Mi } }
env:
  ASPNETCORE_ENVIRONMENT: Production
  Features__NewPricing: "true"
```

| Secret strategy           | How it works                             | Verdict                                                        |
|---------------------------|------------------------------------------|----------------------------------------------------------------|
| Plain k8s Secret          | base64 in etcd                           | Base64 is not encryption; enable encryption at rest at minimum |
| Sealed Secrets            | Encrypted in git, decrypted in-cluster   | Good GitOps fit                                                |
| External Secrets Operator | Syncs from Key Vault / Secrets Manager   | Best of both; one source of truth                              |
| CSI Secrets Store driver  | Mounts vault secrets as files            | No secret object in the cluster at all                         |
| Workload identity         | Pod authenticates as itself to the cloud | The strongest option — no secret exists                        |

> **\[Note\]**
>
> **Config that changes without a deploy.** Feature flags (OpenFeature, LaunchDarkly, Azure App Configuration) plus `IOptionsMonitor` let you turn behaviour on and off in seconds. That decouples *deploy* from *release*, which is the single biggest reduction in deployment risk.

###### The layering, and injecting secrets safely

``` text
  IConfiguration is an ORDERED CHAIN. Later sources win.

    1  appsettings.json                    committed, defaults
    2  appsettings.{Environment}.json      committed, per-environment shape
    3  User Secrets                        DEVELOPMENT ONLY, outside the repo
    4  environment variables               how containers are configured
    5  a secret store (Key Vault, etc.)    production secrets
    6  command line                        overrides everything

  the naming rule that trips people up:
    "ConnectionStrings:Default"  in JSON
    ConnectionStrings__Default   as an environment variable
                     ^^ DOUBLE UNDERSCORE for the section separator
```

``` bash
# development: never a secret in appsettings.json, even in dev
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;..."
# stored in your user profile, outside the repository, impossible to commit
```

``` csharp
# Kubernetes: mount secrets as FILES, not environment variables
volumeMounts:
  - name: secrets
    mountPath: /run/secrets
    readOnly: true
# and read them with the key-per-file provider
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

# why files beat env vars for secrets:
#   env vars appear in crash dumps, in `ps` output, in child processes,
#   in error-tracking payloads, and in the Aspire dashboard.
#   a mounted file is readable only by the process, and can be rotated
#   WITHOUT A RESTART.

# best of all: no stored secret. External Secrets Operator or the CSI driver
# fetches from Key Vault using the pod's WORKLOAD IDENTITY.
```

``` text
# Helm: values per environment, ONE chart
# values-prd.yaml
replicaCount: 3
image: { repository: acme.azurecr.io/orders-api, tag: "" }   # tag from --set
resources:
  requests: { cpu: 250m, memory: 512Mi }
  limits:   { memory: 512Mi }        # memory limit yes, CPU limit deliberately not
env:
  ASPNETCORE_ENVIRONMENT: Production
  Logging__LogLevel__Default: Warning

# helm upgrade --install orders ./chart -f values-prd.yaml \
#   --set image.tag=$GIT_SHA --atomic --timeout 5m
# --atomic rolls back automatically if the release does not become healthy.
```

> **\[Note\]**
>
> **Validate configuration at startup.** `AddOptions<T>().ValidateOnStart()` means the application refuses to boot with a missing or invalid setting, which the deployment then catches. The alternative — failing on the first request that happens to need it — means a bad config reaches users and looks like an application bug.

### Compute: VMs to Serverless

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Compute: VMs to Serverless</p>

<p class="pd">Where code runs, from a physical machine to a function that exists for 40 milliseconds — and how to choose without following fashion.</p>

<a id="be-g665"></a>

#### Orchestration: the concepts that matter

Kubernetes has a large surface area and a small conceptual core. These are the objects you meet daily.

| Object                     | Is                                                             | Analogy                                              |
|----------------------------|----------------------------------------------------------------|------------------------------------------------------|
| **Pod**                    | One or more containers sharing a network namespace and storage | The smallest deployable unit — usually one container |
| **Deployment**             | Declares N replicas of a pod, and manages rollout              | “Keep 3 of these running, updated this way”          |
| **Service**                | A stable virtual IP and DNS name in front of pods              | An internal load balancer                            |
| **Ingress** / **Gateway**  | External HTTP routing into services                            | The reverse proxy                                    |
| **ConfigMap** / **Secret** | Configuration and credentials injected as env vars or files    | `appsettings.json` and a vault                       |
| **StatefulSet**            | Pods with stable identities and persistent volumes             | For databases and anything with durable local state  |
| **Job** / **CronJob**      | Run to completion, once or on a schedule                       | Migrations, batch work                               |
| **HPA**                    | Scales replicas on a metric                                    | Autoscaling                                          |
| **PVC**                    | A claim on persistent storage                                  | A disk that survives pod restarts                    |

``` text
apiVersion: apps/v1
kind: Deployment
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate: { maxSurge: 1, maxUnavailable: 0 }   # never below capacity
  template:
    spec:
      terminationGracePeriodSeconds: 45      # must EXCEED your drain time
      containers:
      - name: api
        image: registry/api@sha256:abc123...
        ports: [{ containerPort: 8080 }]

        # requests = what the scheduler reserves.  limits = the hard cap.
        resources:
          requests: { cpu: "250m", memory: "512Mi" }
          limits:   { memory: "512Mi" }      # note: NO cpu limit - see below

        startupProbe:                         # protects a slow start
          httpGet: { path: /health/live, port: 8080 }
          failureThreshold: 30
          periodSeconds: 2
        livenessProbe:                        # restart if this fails
          httpGet: { path: /health/live, port: 8080 }
          periodSeconds: 10
        readinessProbe:                       # remove from the Service if this fails
          httpGet: { path: /health/ready, port: 8080 }
          periodSeconds: 5

        lifecycle:
          preStop:
            exec: { command: ["sleep", "10"] }   # let endpoints propagate first
```

###### The three configuration decisions that cause most Kubernetes pain

**1. CPU limits.** Setting a CPU limit causes cgroup throttling, and throttling shows up as latency with apparently spare CPU. Common current guidance is to set CPU *requests* (so the scheduler places pods sensibly) but omit CPU *limits*, while always setting a memory limit — memory is incompressible, so an unbounded pod can take down its node.

**2. Liveness probes that check dependencies.** A liveness probe that returns unhealthy because the database is slow will restart every replica simultaneously, during a database incident, making it worse. Liveness must only ask “is this process alive?” Dependency checks belong in readiness.

**3. The shutdown race.** When a pod is deleted, two things happen concurrently: it is removed from endpoints, and it receives `SIGTERM`. Endpoint removal has to propagate to every kube-proxy and ingress, which takes a second or two — so traffic can still arrive after `SIGTERM`.

``` text
# the fix: a preStop sleep, so the pod keeps serving while endpoints propagate
lifecycle:
  preStop:
    exec: { command: ["sleep", "10"] }

# sequence with the sleep in place:
#   t=0   pod marked Terminating; endpoint removal begins; preStop sleep starts
#   t=10  sleep ends -> SIGTERM sent. By now no new traffic is being routed.
#   t=10+ application drains in-flight requests and exits
#   terminationGracePeriodSeconds must exceed sleep + drain time,
#   or the pod is SIGKILLed mid-drain.
```

| Symptom                         | Cause                                                         |
|---------------------------------|---------------------------------------------------------------|
| 502s during every deploy        | No `preStop`, or the app ignores `SIGTERM`                    |
| Pods restart in a loop          | Liveness probe too aggressive, or no `startupProbe`           |
| `OOMKilled`, exit code 137      | Memory limit below actual usage                               |
| Pod `Pending` forever           | No node satisfies its resource requests                       |
| Latency high, CPU low           | CPU limit causing throttling                                  |
| Works, then fails after scaling | Requests set far below real usage, so nodes are overcommitted |

### Kubernetes in Depth

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Kubernetes in Depth</p>

<p class="pd">Kubernetes past the object list: how scheduling, networking, storage and upgrades actually behave, and the operational realities that decide whether running it is worth it.</p>

<a id="be-g683"></a>

#### Should you run Kubernetes at all?

The honest question first, because the answer is frequently no and the cost of getting it wrong is a platform team you did not budget for.

**Kubernetes earns its cost when**

- Many services, deployed independently, by several teams
- Genuine bin-packing savings across a large fleet
- You need portability across clouds or on-premises
- The ecosystem is the point — operators, mesh, GitOps
- Someone's job is the platform

**Something simpler is better when**

- Under roughly ten services
- One team
- A managed container service does the job (Cloud Run, Container Apps, ECS Fargate)
- Nobody's job is the platform — it will become everybody's problem
- You are running one monolith and a database

| Alternative                         | Gives you                                          | Gives up                                       |
|-------------------------------------|----------------------------------------------------|------------------------------------------------|
| **Cloud Run / Container Apps**      | Containers, autoscaling, scale-to-zero, HTTPS      | Fine-grained scheduling, operators, DaemonSets |
| **ECS on Fargate**                  | Containers with no nodes to manage                 | The Kubernetes ecosystem                       |
| **App Service / Elastic Beanstalk** | Deploy code, platform handles the rest             | Container-level control                        |
| **Nomad**                           | Simpler orchestration, non-container workloads too | Ecosystem size                                 |
| **VMs + systemd + a load balancer** | Total simplicity, complete understanding           | Density, self-healing, declarative rollout     |

> **\[Note\]**
>
> **The realistic cost.** A production Kubernetes platform means cluster upgrades every few months, CNI and CSI driver compatibility, ingress controllers, certificate automation, RBAC, network policy, autoscaler tuning, observability for the cluster itself, and a rota of people who understand it. That is a role, not a task. If you cannot name who holds it, choose a managed container service instead — you will get most of the benefit and almost none of the cost.

<a id="be-g684"></a>

#### Scheduling and placement

The scheduler decides which node runs each pod. Understanding it explains most “why is this pod `Pending`” and “why did everything land on one node” questions.

``` text
  the scheduling decision, in two phases

  FILTER    which nodes COULD run this pod?
              enough allocatable cpu/memory for its REQUESTS
              node selector / affinity matches
              taints tolerated
              required volumes attachable in that zone
              ports available

  SCORE     of those, which is BEST?
              least allocated, or most allocated (bin-packing), by policy
              spreading across zones and nodes
              image already present on the node
              affinity/anti-affinity preferences

  and the fact that surprises people:
    scheduling uses REQUESTS, not actual usage.
    a pod requesting 100m CPU but using 2 cores is scheduled as if it
    needed 100m - so a node can be "full" at 10% real utilisation,
    or catastrophically overcommitted at 100% requested.
```

``` text
# spread replicas across zones AND nodes, so one failure cannot take all of them
spec:
  topologySpreadConstraints:
  - maxSkew: 1
    topologyKey: topology.kubernetes.io/zone
    whenUnsatisfiable: DoNotSchedule       # hard: refuse rather than bunch up
    labelSelector: { matchLabels: { app: api } }
  - maxSkew: 1
    topologyKey: kubernetes.io/hostname
    whenUnsatisfiable: ScheduleAnyway      # soft: prefer, but do not block

# and a PodDisruptionBudget, or a node drain can take every replica at once
apiVersion: policy/v1
kind: PodDisruptionBudget
spec:
  minAvailable: 2                # or maxUnavailable: 1
  selector: { matchLabels: { app: api } }
# without a PDB, a cluster upgrade draining nodes one at a time can still
# evict all three replicas within seconds if they share a node.
```

| Mechanism                   | Effect                                                                    |
|-----------------------------|---------------------------------------------------------------------------|
| `nodeSelector`              | Simple label match — hard requirement                                     |
| `nodeAffinity`              | Richer expressions, required or preferred                                 |
| `podAntiAffinity`           | Keep replicas apart — expensive to evaluate at scale                      |
| `topologySpreadConstraints` | The modern, cheaper way to spread. Prefer this                            |
| **Taints and tolerations**  | The node *repels* pods unless they tolerate it — for dedicated node pools |
| `priorityClassName`         | Higher-priority pods can preempt lower ones under pressure                |
| **Pod overhead**            | Accounts for the runtime's own cost in scheduling                         |

> **\[Trap\]**
>
> **Common trap.** Setting requests far below actual usage is the most common cause of mysterious cluster instability. The scheduler packs nodes based on requests, so a node accepting ten pods that each requested 100m but use a full core will thrash, throttle, and eventually evict pods under memory pressure. Set requests from measured usage — the Vertical Pod Autoscaler in recommendation mode will tell you what they should be.

###### Diagnosing a pod that will not start, in order

``` bash
# the sequence, and each step eliminates a layer
kubectl get pod <pod> -o wide            # what STATE, on which node
kubectl describe pod <pod>               # EVENTS at the bottom - read these FIRST
kubectl logs <pod> --previous            # the PREVIOUS container's logs.
                                         # --previous is the flag people forget,
                                         # and it is the only way to see why a
                                         # CrashLooping container died.
kubectl logs <pod> -c <init-container>   # init containers have separate logs
kubectl get events --sort-by=.lastTimestamp | tail -30
```

| Status                    | Means                            | Look at                                                                        |
|---------------------------|----------------------------------|--------------------------------------------------------------------------------|
| `Pending`                 | Not scheduled onto any node      | `describe` events — insufficient CPU/memory, taints, no matching node selector |
| `ContainerCreating`       | Scheduled, still starting        | Volume attachment, secret or configmap missing                                 |
| `ImagePullBackOff`        | Cannot fetch the image           | Tag typo, private registry credentials, network policy                         |
| `CrashLoopBackOff`        | Starts, exits, repeats           | `logs --previous`. Usually config or a missing dependency                      |
| `OOMKilled`               | Exceeded its memory limit        | Raise the limit, or fix the leak. Exit code 137                                |
| `Running` but not `Ready` | The readiness probe fails        | The probe path, its timeout, and whether a dependency is down                  |
| `Terminating`, stuck      | A finalizer, or the grace period | `preStop` hooks and `terminationGracePeriodSeconds`                            |

``` text
  THE THREE MISCONFIGURATIONS THAT CAUSE MOST KUBERNETES INCIDENTS

  1  A CPU LIMIT, SET CASUALLY

       resources: { limits: { cpu: "500m" } }

     CPU limits are enforced by cgroup THROTTLING over a 100 ms period.
     at 500m the container gets 50 ms of every 100 ms - and when it
     exhausts that, it is FROZEN until the next period.

     symptom: p99 latency in whole multiples of 100 ms, while CPU
              usage reads well BELOW the limit. It looks like the app
              is slow. It is being stopped.

     -> set CPU REQUESTS (for scheduling). Usually omit CPU LIMITS.
        ALWAYS set memory limits - memory is not throttled, it is killed.

  2  A LIVENESS PROBE THAT CHECKS DEPENDENCIES

       livenessProbe: { httpGet: { path: /health } }   # and /health hits the DB

     the database has a slow minute -> EVERY pod fails liveness ->
     Kubernetes RESTARTS THEM ALL, simultaneously -> they reconnect at
     once, hammering a database that was already struggling.

     a brief blip becomes a full self-inflicted outage.

     -> liveness checks NOTHING external. Readiness may check dependencies.

  3  NO preStop, SO SHUTDOWN RACES ENDPOINT REMOVAL

     pod deletion sends SIGTERM and removes the endpoint CONCURRENTLY.
     the endpoint removal must propagate to every kube-proxy, which
     takes a second or two - during which traffic still arrives at a
     pod that is already shutting down.

     -> lifecycle: { preStop: { exec: { command: ["sleep","5"] } } }
        sleep FIRST, so the endpoint is gone before the app stops.
```

<a id="be-g685"></a>

#### Networking inside a cluster

``` text
  four distinct address spaces, and confusing them is common

  NODE network      real VPC addresses. 10.0.16.0/20
  POD network       every pod gets an IP. With most cloud CNIs these are
                    REAL VPC addresses too - which is why clusters exhaust
                    subnets fast. With overlay CNIs they are virtual.
  SERVICE network   VIRTUAL. 10.96.0.0/12 by default. These addresses exist
                    nowhere - they are iptables/IPVS/eBPF rules that rewrite
                    the destination to a pod IP.
  CLUSTER DNS       resolves service names to service VIPs

  so: a Service IP is not pingable from outside the cluster, does not appear
  on any interface, and cannot be reached by anything not running the
  kube-proxy rules. This is why "the service IP does not respond" from a
  bastion is expected rather than broken.
```

| Service type              | Reachable from                  | Note                                                                     |
|---------------------------|---------------------------------|--------------------------------------------------------------------------|
| `ClusterIP`               | Inside the cluster              | The default                                                              |
| `NodePort`                | Any node IP on a high port      | Crude; mostly a building block                                           |
| `LoadBalancer`            | The internet, or an internal LB | Provisions a cloud load balancer — one per service, which gets expensive |
| `ExternalName`            | —                               | A CNAME to something outside the cluster                                 |
| **Headless**              | Inside                          | Returns pod IPs directly — what StatefulSets use                         |
| **Ingress / Gateway API** | The internet                    | One load balancer, many services routed by host and path                 |

``` text
# Gateway API is replacing Ingress. It separates infrastructure from routing,
# which matters because they are usually owned by different teams.
apiVersion: gateway.networking.k8s.io/v1
kind: Gateway                       # platform team owns this
spec:
  gatewayClassName: cloud-lb
  listeners:
  - name: https
    port: 443
    protocol: HTTPS
    tls: { certificateRefs: [{ name: wildcard-tls }] }
---
apiVersion: gateway.networking.k8s.io/v1
kind: HTTPRoute                     # application team owns this
spec:
  parentRefs: [{ name: main-gateway }]
  hostnames: ["api.example.com"]
  rules:
  - matches: [{ path: { type: PathPrefix, value: /orders } }]
    backendRefs: [{ name: orders, port: 8080 }]
```

| Symptom                               | Cause                                                              |
|---------------------------------------|--------------------------------------------------------------------|
| Service resolves but connections hang | NetworkPolicy blocking, or no ready endpoints                      |
| `Endpoints` empty                     | Readiness probe failing, or the selector does not match pod labels |
| Intermittent 502 through the ingress  | Pod terminating before endpoint removal propagated                 |
| DNS lookups slow inside the cluster   | CoreDNS under-provisioned, or `ndots:5` causing extra queries      |
| Cross-namespace call refused          | NetworkPolicy, or the wrong FQDN                                   |
| Pods cannot be scheduled: no IPs      | Pod CIDR exhausted — a real limit with VPC-native CNIs             |

###### What actually happens to a packet, and where DNS goes wrong

``` text
  a call to http://orders:8080 from inside a pod

  1  DNS
       "orders" is not a hostname. The pod's /etc/resolv.conf has:
         search default.svc.cluster.local svc.cluster.local cluster.local
         options ndots:5

       ndots:5 means: any name with FEWER THAN 5 DOTS is tried against
       EVERY search domain FIRST, before being tried as absolute.

       so "api.example.com" (2 dots) generates:
         api.example.com.default.svc.cluster.local   NXDOMAIN
         api.example.com.svc.cluster.local           NXDOMAIN
         api.example.com.cluster.local               NXDOMAIN
         api.example.com                             <- finally
       FOUR lookups for every external call, x2 for A and AAAA = 8.
       -> this is a real and common source of latency and CoreDNS load.
       -> fix: use a trailing dot ("api.example.com.") or lower ndots.

  2  the name resolves to a CLUSTER IP - a VIRTUAL address.
       nothing listens on it. It exists only in iptables/IPVS rules.

  3  kube-proxy rules on the NODE rewrite the destination to a real
       pod IP, chosen at random from the READY endpoints.

  4  the packet goes to that pod, possibly on another node, via the CNI.

  consequences that follow directly:
    a Service does no health checking - READINESS controls the endpoint list
    load balancing is RANDOM per connection, not per request
      -> long-lived HTTP/2 or gRPC connections PIN to one pod and stay
         there. This is why gRPC in Kubernetes needs client-side load
         balancing or a mesh; a Service alone will not spread it.
```

``` bash
# diagnosing it, in order
kubectl exec -it <pod> -- cat /etc/resolv.conf
kubectl exec -it <pod> -- getent hosts orders
kubectl get endpoints orders          # EMPTY = no READY pods. The most
                                      # common cause of "service unreachable"
kubectl get endpointslices -l kubernetes.io/service-name=orders

# is it DNS, or the network?
kubectl exec -it <pod> -- nc -vz orders 8080
kubectl exec -it <pod> -- nc -vz 10.96.0.14 8080     # by cluster IP
kubectl exec -it <pod> -- nc -vz 10.244.2.7 8080     # by pod IP directly
# each step eliminates a layer: DNS, then Service rules, then the CNI.
```

> **\[Trap\]**
>
> **Common trap.** An empty `Endpoints` list is the single most common cause of “the service does not work”, and it almost never means a networking problem. It means **no pod is passing its readiness probe** — so the Service correctly has nowhere to send traffic. Check readiness before investigating DNS, CNI or policy.

<a id="be-g686"></a>

#### Storage, StatefulSets and running databases

Kubernetes was designed for stateless workloads and has since grown reasonable support for state. Whether to use it for a production database is still a genuine decision, not a foregone one.

``` text
apiVersion: apps/v1
kind: StatefulSet
spec:
  serviceName: postgres              # a HEADLESS service - stable DNS per pod
  replicas: 3
  volumeClaimTemplates:              # one PVC PER POD, not shared
  - metadata: { name: data }
    spec:
      accessModes: [ReadWriteOnce]
      storageClassName: fast-ssd
      resources: { requests: { storage: 500Gi } }

# what a StatefulSet gives you that a Deployment does not:
#   stable names           postgres-0, postgres-1, postgres-2
#   stable DNS             postgres-0.postgres.ns.svc.cluster.local
#   stable storage         postgres-0 always reattaches to ITS volume
#   ordered operations     created and updated 0, 1, 2; deleted in reverse
```

| Access mode        | Means                            | Reality                                      |
|--------------------|----------------------------------|----------------------------------------------|
| `ReadWriteOnce`    | Mounted read-write by one *node* | Block storage. What databases use            |
| `ReadWriteOncePod` | One *pod*, strictly              | Prevents the split-brain case RWO allows     |
| `ReadOnlyMany`     | Many nodes, read only            | Rare                                         |
| `ReadWriteMany`    | Many nodes, read-write           | Network file storage — **not for databases** |

**Running a database in Kubernetes**

- One deployment model for everything
- Operators automate failover, backup and upgrades
- Good for development and test environments
- Sensible when you already run a platform team
- Portable across clouds

**Using a managed database instead**

- Failover, backups and patching are the provider's problem
- No CSI driver, volume attachment or node-drain hazards
- Storage performance is predictable
- Far fewer ways to lose data
- **The default recommendation for production**

> **\[Trap\]**
>
> **Common trap.** Volume attachment is where Kubernetes databases lose data. A node becomes unreachable but is still alive; the controller cannot detach its volume; the pod stays `Terminating` and its replacement stays `Pending`. Force-deleting the pod so the replacement can attach can produce two processes writing the same volume. Use `ReadWriteOncePod`, use an operator that handles fencing, and never force-delete a stateful pod as a reflex.

<a id="be-g687"></a>

#### Cluster upgrades and day-two operations

``` text
  what a cluster upgrade actually involves

  1  control plane      managed: a button, and a version skew policy to respect
  2  node pools         each node: CORDON, DRAIN, replace, uncordon
                        draining evicts every pod, respecting PodDisruptionBudgets
  3  add-ons            CNI, CSI, ingress controller, metrics-server, cert-manager
                        -> each has its OWN supported-version matrix
  4  API deprecations   removed APIs break manifests that still reference them

  the version skew rule: kubelet may trail the API server, never lead.
  so the control plane goes FIRST, then nodes.
  and you cannot skip minor versions - 1.28 to 1.31 is three upgrades.

  cadence: roughly three releases a year, ~14 months of support each.
  falling two versions behind turns a routine task into a project.
```

``` bash
# find manifests using APIs that are about to disappear - BEFORE upgrading
kubectl-convert --output-version apps/v1 -f deployment.yaml
pluto detect-files -d ./manifests            # scans for deprecated APIs
kubectl get --raw /metrics | grep apiserver_requested_deprecated_apis
# the last one tells you what is ACTUALLY still calling deprecated APIs,
# including from controllers you did not write.

# drain a node safely
kubectl cordon node-1                        # no new pods
kubectl drain node-1 --ignore-daemonsets --delete-emptydir-data --timeout=300s
# drain BLOCKS on PodDisruptionBudgets - which is the point.
# a PDB of minAvailable: 1 with 1 replica blocks the drain forever:
# a very common cause of a stuck upgrade.
```

| Day-two concern      | Practice                                                                                   |
|----------------------|--------------------------------------------------------------------------------------------|
| Upgrades             | Every 3–4 months, non-production first, on a schedule you defend                           |
| Add-on compatibility | Check the matrix before, not during                                                        |
| Node image patching  | Replace nodes rather than patching in place — immutable infrastructure                     |
| Autoscaler tuning    | Cluster Autoscaler or Karpenter; watch for scale-down blocked by unevictable pods          |
| Cost                 | Idle node capacity is the usual waste; right-size requests first                           |
| Etcd                 | Managed for you in a cloud cluster. Self-managed: back it up, and test the restore         |
| Certificates         | Internal cluster certificates expire — managed services rotate them; self-managed does not |
| Configuration drift  | GitOps (Argo CD, Flux) so the cluster matches a repository, verifiably                     |

> **\[Note\]**
>
> **GitOps is the practice worth adopting.** The cluster's desired state lives in Git; a controller reconciles continuously. That gives you an audit trail, review on infrastructure changes, drift detection and correction, and a rollback that is a revert. It also removes the category of incident where someone `kubectl apply`'d a fix that nobody recorded and the next deploy silently undid.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a210"></a>

#### Kubernetes scheduling, placement and stateful workloads

Deployments and probes are where most teams stop. The next layer — where pods land and what happens when a node disappears — is what separates a cluster that survives maintenance from one that has an incident every time a node is drained.

``` text
spec:
  template:
    spec:
      # 1. spread replicas across zones, so one zone failing does not take the service down
      topologySpreadConstraints:
        - maxSkew: 1
          topologyKey: topology.kubernetes.io/zone
          whenUnsatisfiable: ScheduleAnyway     # DoNotSchedule if you would rather not run
          labelSelector: { matchLabels: { app: orders-api } }

      # 2. keep replicas off the same node
      affinity:
        podAntiAffinity:
          preferredDuringSchedulingIgnoredDuringExecution:
            - weight: 100
              podAffinityTerm:
                topologyKey: kubernetes.io/hostname
                labelSelector: { matchLabels: { app: orders-api } }

        # 3. and only on nodes suited to the workload
        nodeAffinity:
          requiredDuringSchedulingIgnoredDuringExecution:
            nodeSelectorTerms:
              - matchExpressions:
                  - { key: workload, operator: In, values: [general] }

      # 4. tolerate a dedicated node pool's taint
      tolerations:
        - { key: workload, operator: Equal, value: general, effect: NoSchedule }

      priorityClassName: business-critical      # 5. who gets evicted first under pressure
```

``` text
# 6. and never let voluntary disruption take the service below quorum
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata: { name: orders-api }
spec:
  minAvailable: 2                    # or maxUnavailable: 1
  selector: { matchLabels: { app: orders-api } }
```

| Mechanism                      | Answers                                | Failure without it                                       |
|--------------------------------|----------------------------------------|----------------------------------------------------------|
| Topology spread                | Are replicas in different zones?       | All three pods in one zone; the zone fails; total outage |
| Pod anti-affinity              | Are replicas on different nodes?       | All pods on one node; the node is drained; total outage  |
| Node affinity / selectors      | Is this pod on suitable hardware?      | A memory-heavy service on a burstable node               |
| Taints and tolerations         | Who is allowed on this node pool?      | Batch jobs scheduled onto the latency-sensitive pool     |
| PriorityClass                  | Who survives node pressure?            | A critical API evicted so a nightly report could run     |
| PodDisruptionBudget            | How many can drain at once?            | A cluster upgrade drains every replica simultaneously    |
| `ResourceQuota` / `LimitRange` | Can one namespace consume the cluster? | One team's runaway job starves everyone                  |

###### Stateful workloads, and when not to run them here

A `StatefulSet` gives pods stable names, stable storage and ordered rollout — which is what a database, a broker or anything with a replication protocol needs.

``` text
apiVersion: apps/v1
kind: StatefulSet
metadata: { name: rabbitmq }
spec:
  serviceName: rabbitmq          # headless service: rabbitmq-0.rabbitmq, rabbitmq-1.rabbitmq
  replicas: 3
  podManagementPolicy: OrderedReady
  volumeClaimTemplates:          # each pod gets its OWN persistent volume
    - metadata: { name: data }
      spec:
        accessModes: [ReadWriteOnce]
        storageClassName: premium-ssd
        resources: { requests: { storage: 50Gi } }
```

| Consideration           | Detail                                                                                                           |
|-------------------------|------------------------------------------------------------------------------------------------------------------|
| `ReadWriteOnce` volumes | Attach to one node. A pod cannot move zones without its volume, so zone-aware scheduling is mandatory            |
| Ordered rollout         | Upgrades happen one pod at a time, oldest ordinal last — slow, and correct for quorum systems                    |
| Scaling down            | Does *not* delete the volume claims, by design. Manual cleanup, or you pay for orphaned disks                    |
| Backups                 | Volume snapshots are not application-consistent. You still need a database-aware backup                          |
| Operators               | For PostgreSQL, Kafka or Redis, use the operator (CloudNativePG, Strimzi) rather than a hand-written StatefulSet |

> **\[Note\]**
>
> **The pragmatic default.** Run stateless .NET services on Kubernetes and buy stateful infrastructure as a managed service. Operating a database well — backup verification, failover, version upgrades, performance tuning — is a full-time specialism, and doing it on Kubernetes adds the storage layer's failure modes on top. Run it yourself when there is a specific reason: cost at scale, data residency, or a feature no managed offering provides.

<a id="be-a205"></a>

#### Event-driven autoscaling with KEDA

The Horizontal Pod Autoscaler scales on CPU and memory. For a queue consumer, neither correlates with the thing you care about: a worker waiting on I/O uses almost no CPU while a hundred thousand messages pile up behind it. KEDA scales on the metric that actually matters — queue depth, consumer lag, or anything else you can query.

``` text
  HPA on CPU, for a queue consumer

    queue depth   0 ------ 5,000 ------ 80,000 ------ 80,000
    pod cpu       8% ----- 11% -------- 12% --------- 12%
    replicas      2 ------ 2 ---------- 2 ----------- 2      <- never scales. CPU never moves.

  KEDA on queue length, target 500 messages per replica

    queue depth   0 ------ 5,000 ------ 80,000 ------ drained
    replicas      0 ------ 10 --------- 50 ---------- 0      <- scales to zero when idle
```

``` text
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata: { name: order-worker }
spec:
  scaleTargetRef: { name: order-worker }
  minReplicaCount: 0            # scale to zero when the queue is empty
  maxReplicaCount: 50
  pollingInterval: 15
  cooldownPeriod: 120           # wait before scaling back down
  advanced:
    horizontalPodAutoscalerConfig:
      behavior:
        scaleDown: { stabilizationWindowSeconds: 300 }   # avoid flapping
  triggers:
    - type: rabbitmq
      metadata:
        protocol: amqp
        queueName: orders
        mode: QueueLength
        value: "500"            # target messages per replica
      authenticationRef: { name: rabbit-auth }
```

| Scaler                 | Scales on                        | Common for                                           |
|------------------------|----------------------------------|------------------------------------------------------|
| `rabbitmq`             | Queue length or publish rate     | Task queues                                          |
| `kafka`                | Consumer group lag per partition | Stream processing — note the partition-count ceiling |
| `azure-servicebus`     | Active message count             | Azure workloads                                      |
| `postgresql` / `mssql` | The result of a query            | Outbox depth, pending-job tables                     |
| `prometheus`           | Any PromQL expression            | Anything you already measure — the escape hatch      |
| `cron`                 | A schedule                       | Predictable business-hours capacity                  |

###### What scaling to zero demands of your application

- **Fast startup.** A cold pod that takes 25 seconds to JIT, build the EF model and open connections makes scale-from-zero feel broken. ReadyToRun or a warm-up hosted service matters much more here than on an always-on service.
- **Correct graceful shutdown.** Scale-down happens constantly rather than only on deploys, so a consumer that abandons an in-flight message will lose messages every few minutes instead of every few weeks.
- **Idempotent handlers.** A pod killed mid-message means redelivery. This is the same requirement as always, but now it is exercised continuously.
- **No in-process scheduler.** With replicas varying from 0 to 50, a `PeriodicTimer` in each pod runs the job zero times or fifty. Move schedules to a CronJob or a leader lock.
- **Bounded database connections.** 50 replicas × a pool of 100 is 5,000 connections against a server that permits 200. Set `Max Pool Size` low on workers, or front the database with a pooler.

``` text
// startup cost matters when pods come and go constantly
<PublishReadyToRun>true</PublishReadyToRun>
<TieredPGO>true</TieredPGO>

// and connection pools must be sized for the MAXIMUM replica count
"ConnectionStrings": {
  "Default": "Host=pg;Database=orders;Maximum Pool Size=5;Minimum Pool Size=0"
}
// 50 replicas x 5 = 250. Budget it deliberately rather than discovering it.
```

> **\[Trap\]**
>
> **Common trap.** KEDA scales the deployment, not the work. If a single message takes ten minutes to process, adding replicas does nothing for the message already in flight and the queue drains no faster than one message per worker per ten minutes. Autoscaling fixes throughput, never latency of an individual item — for that you must split the work.

### Shipping it

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Shipping it</p>

<p class="pd">The artefacts that turn working code into a running service: the container, the health endpoints, the observability wiring and the deployment manifest.</p>

<a id="be-w16"></a>

#### The deployment manifest, annotated

``` text
apiVersion: apps/v1
kind: Deployment
metadata: { name: orders-api }
spec:
  replicas: 3
  strategy:
    type: RollingUpdate
    rollingUpdate: { maxSurge: 1, maxUnavailable: 0 }   # never lose capacity
  template:
    spec:
      terminationGracePeriodSeconds: 40                 # > app ShutdownTimeout
      containers:
      - name: api
        image: registry.internal/orders-api@sha256:9f2c…   # digest, not :latest
        ports: [{ containerPort: 8080 }]

        resources:
          requests: { cpu: "250m", memory: "512Mi" }     # what the scheduler reserves
          limits:   { memory: "1Gi" }                    # OOM boundary; note: NO cpu
                                                         # limit — throttling hurts
                                                         # tail latency badly
        env:
        - name: DOTNET_GCHeapHardLimitPercent
          value: "75"                                    # leave room under the limit
        - name: ConnectionStrings__Default
          valueFrom: { secretKeyRef: { name: orders-secrets, key: db } }

        startupProbe:                                    # slow start ≠ dead
          httpGet: { path: /health/live, port: 8080 }
          failureThreshold: 30
          periodSeconds: 2                               # up to 60s to boot
        livenessProbe:
          httpGet: { path: /health/live, port: 8080 }
          periodSeconds: 10
          failureThreshold: 3
        readinessProbe:
          httpGet: { path: /health/ready, port: 8080 }
          periodSeconds: 5
          failureThreshold: 2

        lifecycle:
          preStop:
            exec: { command: ["sleep", "5"] }            # let the LB notice first
        securityContext:
          runAsNonRoot: true
          allowPrivilegeEscalation: false
          readOnlyRootFilesystem: true
          capabilities: { drop: ["ALL"] }
---
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata: { name: orders-api }
spec:
  minAvailable: 2                                        # node drains cannot
  selector: { matchLabels: { app: orders-api } }         # take everything down
```

| Setting                  | Why it is there                                                                                          |
|--------------------------|----------------------------------------------------------------------------------------------------------|
| `maxUnavailable: 0`      | New pod must be ready before an old one goes                                                             |
| `startupProbe`           | Separates “slow to boot” from “broken” — without it a slow migration gets killed in a restart loop       |
| `preStop: sleep 5`       | Endpoint removal is eventually consistent; without the pause, in-flight requests hit a shutting-down pod |
| No CPU limit             | CFS throttling adds hundreds of milliseconds to p99 on bursty workloads                                  |
| Image digest             | The artefact you tested is the artefact that runs                                                        |
| `readOnlyRootFilesystem` | A compromised process cannot write a payload to disk                                                     |
