---
id: "11-3"
number: "11.3"
title: "Containers and Docker"
part: 11
part_title: "Shipping software"
words: 7211
minutes: 31
---

# 11.3 Containers and Docker

> **Quick look.** Packaging a program with everything it needs into a container image.
>
> - Images are content-addressed layers; containers are running instances.
> - Multi-stage Dockerfile: build with the SDK image, run on a small runtime or chiseled image as non-root.
> - Copy the project file and restore before copying source so the restore layer stays cached; use .dockerignore.
> - A secret deleted in a later layer is still in the image: never bake secrets in.
> - Configure through environment variables (Section__Key), keep data in volumes, log to stdout, set resource limits.
> - Server GC with a small memory limit can get OOM-killed; cap the heap if needed.
> - Deploy by digest or never-reused tags; use Compose locally; simpler platforms often beat Kubernetes.


### The big picture

"It works on my machine" is one of the oldest complaints in software, and it has a simple cause. A program depends on far more than its own code: the runtime version, system libraries, configuration files and environment variables. Every machine has a slightly different mix. Containers fix this by packaging a program together with everything it needs into one image. That image runs the same way on a laptop, in a test pipeline and in production. The operating system (1.7) introduced containers as ordinary processes with isolation and limits applied. This chapter is about using them.

It covers images and their layers, writing a Dockerfile for an ASP.NET Core app that builds fast and produces a small, locked-down image, and the build tool that skips the Dockerfile altogether. It then covers running containers with ports, volumes and networks, and running a whole local environment with Docker Compose. It closes with orchestration: running many containers across many machines with Kubernetes, and when a simpler platform is enough. There is no container runtime in the environment where this guide was written. So each file in the chapter was checked with real tools instead: a Dockerfile linter, the official Kubernetes schemas, and the Compose specification.

### Before you start

**Builds on:** 1.7 The operating system, 11.1 Version control with Git, 11.2 Builds and dependencies, 8.3 A modern .NET backend, 8.1 What a backend does, 6.4 DNS.

**Ideas explained later:**

- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Cloud** (11.5): Computing rented on demand from a provider's data centres over the network, billed by use.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Observability** (11.6): Being able to explain a system's behaviour, including new questions, from the signals it emits.
- **YAML** (11.4): A text format for structured data that uses indentation, used for pipelines and many configuration files.
- **AWS** (11.5): Amazon Web Services, the largest cloud provider, launched in 2006.
- **Runner** (11.4): The machine or container that runs a pipeline's jobs.
- **Performance** (12.4): How quickly a system does its work at a given load.

**Tools and technologies named here:**

- **Deploy** (11.4): To install a new version of software where it will run, such as on production servers.
- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between an image and a container?**

> An image is a read-only package: a filesystem of layers plus settings such as the command to run. A container is a running instance of an image, with its own writable layer on top. Many containers can run from one image.

**2. Why copy the project files and restore before copying the rest of the source in a Dockerfile?**

> Each instruction produces a cached layer, reused while its inputs are unchanged. Restoring packages only depends on the project files, so ordinary code changes reuse the cached restore layer, and builds take seconds instead of minutes.

**3. What does a multi-stage build achieve?**

> It compiles in a large image containing the SDK, then copies only the published output into a small runtime image. The final image has no compilers or source code, so it is smaller and has less to attack.

**4. Why deploy images by digest rather than by a tag like latest?**

> A tag is a movable label; `latest` or even `1.4` can point to different images tomorrow. A digest is the hash of the image's content, so it always means exactly the same image.

**5. Why is putting a secret in a Dockerfile a mistake even if a later step deletes it?**

> Every layer is kept in the image, so the secret is still inside, readable by anyone who pulls the image. Secrets must be supplied at run time, from the environment or a secret store.

**6. What does a Kubernetes Deployment do?**

> It declares a desired state, such as three replicas of this image with these settings. Kubernetes keeps making reality match it, replacing crashed pods and rolling out new versions gradually.

**7. What is the difference between a readiness probe and a liveness probe in Kubernetes?**

> A failing readiness probe removes the pod from the service's traffic until it recovers. A failing liveness probe gets the container restarted. They match the readiness and liveness checks from chapter 8.1.

### Images and containers

A **container image** is a read-only package holding everything a program needs to run, apart from the operating system kernel. That means a filesystem containing the program, its runtime and libraries, plus settings such as environment variables, the working directory and the command to start. A container is a running instance of an image: the processes from The operating system (1.7), isolated with namespaces and limited with cgroups, seeing the image's filesystem as their own, with a thin writable layer on top. Start ten containers from one image and you get ten isolated copies of the same program.

**Docker**, released in 2013, made containers practical for everyday use, with a simple way to build images and run them. Its image format became the Open Container Initiative (OCI) standard, so images built with Docker run under Kubernetes, Podman and every cloud container service.

An image is built from **image layers**: each is a set of file changes, added, modified or deleted, stacked in order to form the final filesystem. Layers are named by the hash of their content, like Git's objects in Version control with Git (11.1). Images that share a **base image**, such as the official ASP.NET Core runtime, share those layers on disk and in transfers, and pulling a new version of your app downloads only the layers that changed.

### Registries, tags and digests

Images are stored in a **container registry**: Docker Hub, Microsoft's registry at `mcr.microsoft.com` for official .NET images, or a private registry such as Azure Container Registry, Amazon ECR or GitHub Container Registry. An image is named by its repository and a tag, as in `airline.azurecr.io/bookings-api:1.4.2`.

An **image tag** is a movable label, like a Git branch: `latest` and even `1.4` can point to a different image tomorrow. An **image digest**, written `@sha256:...`, is the hash of the image's content, so it always refers to exactly the same bytes. Build pipelines tag images for people to read and deploy them by digest, or with tags that are never reused, such as the version and commit, so production runs exactly what was tested.

### Writing a Dockerfile

A **Dockerfile** is a list of instructions that builds an image. `FROM` picks the base image. `WORKDIR` sets the working folder. `COPY` copies files in from the **build context**, the folder sent to the builder, and `RUN` runs a command during the build. `ENV` sets environment variables, `EXPOSE` documents the port, `USER` sets which user runs the program, and `ENTRYPOINT` gives the command to start.

This Dockerfile builds the bookings API from Builds and dependencies (11.2):

```dockerfile
# Build stage: the full SDK, used only to compile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/Bookings.Api/Bookings.Api.csproj src/Bookings.Api/packages.lock.json src/Bookings.Api/
COPY src/Bookings.Domain/Bookings.Domain.csproj src/Bookings.Domain/packages.lock.json src/Bookings.Domain/
RUN dotnet restore src/Bookings.Api/Bookings.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/Bookings.Api/Bookings.Api.csproj -c Release -o /app --no-restore

# Runtime stage: only the ASP.NET Core runtime and the published app, no shell, no package manager
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bookings.Api.dll"]
```

It is a **multi-stage build**. The first stage starts from the .NET SDK image, which is large, because it contains compilers and build tools, and uses it to restore and publish. The second stage starts from the much smaller ASP.NET Core runtime image, and copies in only the published output. Nothing from the first stage reaches the final image unless it is explicitly copied: no SDK, no source code and no intermediate files.

The order of the steps matters because of layer caching. Each instruction creates a layer, and the builder reuses a cached layer whenever that instruction and everything it depends on are unchanged. Copying only the project files, the lock files and the shared build settings first, then restoring, means the slow restore layer is reused on every build until a package changes. Ordinary code changes only redo the steps after `COPY src/`. Copying everything first would re-download every package for every one-line change.

The runtime image here is one of Microsoft's chiseled images: stripped-down Ubuntu images with no shell, no package manager and only the libraries .NET needs. They are a form of **distroless image**, and have far less to attack or patch, and they run as a non-root user by default. `USER $APP_UID` makes that explicit. The official .NET 8 and later images listen on port 8080 by default, because ports below 1024 need root, as A modern .NET backend (8.3) mentioned.

A `.dockerignore` file keeps unwanted files out of the build context, making builds faster and keeping secrets and local settings out of the image:

```text
**/bin/
**/obj/
**/node_modules/
.git/
.vs/
*.user
**/appsettings.Development.json
.env
```

The .NET SDK can also build images without a Dockerfile. `dotnet publish -t:PublishContainer` creates an OCI image directly, from the official base images, with properties such as `ContainerRepository` and `ContainerImageTags` in the project file. It suits straightforward services, and a Dockerfile remains the choice when the image needs anything unusual.

### Running containers

`docker run` starts a container from an image. `-p 8080:8080` publishes a container port on the host. `-e Name=value` sets an environment variable, which is how configuration gets in, following the twelve-factor ideas from What a backend does (8.1). ASP.NET Core reads nested settings from variables with double underscores, such as `ConnectionStrings__Bookings`.

A container's writable layer disappears with the container, so data that must survive goes in a **volume**: storage managed by the container runtime and mounted into the container at a path, such as a database's data folder. Bind mounts map a folder from the host instead, which is useful during development.

Containers on the same network reach each other by name through built-in DNS, from DNS (6.4): the API connects to `db:1433`, not an IP address. Containers should write logs to standard output, where the runtime collects them, as Observability (11.6) describes. And they should run with resource limits. The .NET garbage collector and thread pool read the container's CPU and memory limits and size themselves to fit, as Memory management and garbage collection (2.7) described.

### A local environment with Compose

**Docker Compose** describes several containers that run together, their configuration, networks and volumes, in one YAML file. `docker compose up` starts them all. It is the easiest way to give every developer the same local environment: the API, its database and its cache.

```yaml
# compose.yaml: the API with its database and cache, for local development: docker compose up --build
# SA_PASSWORD comes from a .env file next to this one, which is never committed
services:
  api:
    build: .
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Bookings: "Server=db;Database=Bookings;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=true"
      ConnectionStrings__Redis: "cache:6379"
    depends_on:
      db:
        condition: service_healthy
      cache:
        condition: service_started
  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: ${SA_PASSWORD}
    volumes:
      - sqldata:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -C -Q 'SELECT 1' || exit 1"]
      interval: 10s
      retries: 10
  cache:
    image: redis:7.4
volumes:
  sqldata:
```

`depends_on` with `service_healthy` waits for the database's health check to pass before starting the API. Values such as `${SA_PASSWORD}` come from a `.env` file beside the compose file, listed in `.dockerignore` and `.gitignore`, so the password is never committed, as Secrets and cryptography basics (10.5) required. The named volume keeps the database's data between restarts. Microsoft's Aspire tooling, from A modern .NET backend (8.3), does a similar job in C# for .NET solutions, with a dashboard for logs and traces.

### Container security

Images are software you ship, and they need the same care.

Keep images small and minimal: chiseled or distroless runtime images have fewer packages with vulnerabilities to patch. Scan images for known vulnerabilities in the pipeline, with tools such as Trivy or the scanners built into cloud registries. Rebuild images regularly, not only when your code changes, because base images get security fixes. Pin base images by digest, or at least by a specific version, and update them deliberately.

Never put secrets in images. Every layer is kept, so a secret copied in and deleted in a later step is still inside the image for anyone who pulls it. Supply secrets at run time. Run as a non-root user, with a read-only root filesystem where possible, and without extra privileges, as the Kubernetes example below does. Signing images with tools such as cosign or Notation, and attaching an SBOM, from Builds and dependencies (11.2), lets the platform refuse images that didn't come from your pipeline.

### Orchestration

One container on one machine is easy. Running a service as many containers across many machines is harder. Something must choose machines, restart crashed containers, replace failed machines, route traffic only to healthy copies, and roll out new versions without downtime. That is **container orchestration**, and **Kubernetes** is the dominant orchestrator. It began at Google, was released as open source in 2014, and is now run by every major cloud as a managed service.

Kubernetes is declarative. You describe the desired state in YAML: run three replicas of this image, with these settings, reachable at this name. Its controllers continuously compare actual state with desired state and act to close the gap, a loop called reconciliation. Kill a container and a new one appears; delete a machine and its work moves elsewhere.

Its main objects fit together like this. A cluster is a set of machines, called nodes, managed together. A **pod** is the smallest unit Kubernetes runs: one or a few containers that share a network address and are scheduled together. A **Kubernetes Deployment** declares how many replicas of a pod should run and manages rolling updates between versions. A **Kubernetes Service** gives a set of pods one stable name and virtual address, and spreads traffic across the ready ones. An ingress, or the newer Gateway API, routes HTTP traffic from outside the cluster to services, often terminating TLS. ConfigMaps and Secrets hold configuration and secrets for pods to read. Namespaces divide a cluster between teams or environments.

This manifest runs the bookings API as three replicas behind a service:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: bookings-api
  labels:
    app: bookings-api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: bookings-api
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxUnavailable: 0          # never drop below three ready copies during a rollout
      maxSurge: 1
  template:
    metadata:
      labels:
        app: bookings-api
    spec:
      containers:
        - name: api
          image: airline.azurecr.io/bookings-api:1.4.2
          ports:
            - containerPort: 8080
          env:
            - name: ConnectionStrings__Bookings
              valueFrom:
                secretKeyRef:
                  name: bookings-db
                  key: connection-string
          resources:
            requests:
              cpu: 250m
              memory: 256Mi
            limits:
              memory: 512Mi
          readinessProbe:
            httpGet:
              path: /health/ready
              port: 8080
            periodSeconds: 5
          livenessProbe:
            httpGet:
              path: /health/live
              port: 8080
            periodSeconds: 10
            failureThreshold: 3
          securityContext:
            runAsNonRoot: true
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem: true
          volumeMounts:
            - name: tmp
              mountPath: /tmp
      volumes:
        - name: tmp
          emptyDir: {}
---
apiVersion: v1
kind: Service
metadata:
  name: bookings-api
spec:
  selector:
    app: bookings-api
  ports:
    - port: 80
      targetPort: 8080
```

The probes connect to the health endpoints from What a backend does (8.1). A **Kubernetes probe** is a check Kubernetes runs against each container. While the readiness probe fails, the pod gets no traffic, which covers start-up and temporary trouble. When the liveness probe fails three times in a row, the container is restarted. The rolling update settings replace pods one at a time and never let ready replicas drop below three. The resource requests tell the scheduler how much CPU and memory to reserve, and the memory limit stops a leaking process from starving its neighbours. A horizontal pod autoscaler can add and remove replicas as load changes.

When a pod is stopped, for a rollout or a scale-down, Kubernetes stops sending it traffic and sends its processes SIGTERM, from The operating system (1.7), then waits for a grace period, 30 seconds by default, before forcing them to stop. ASP.NET Core's host handles SIGTERM by finishing in-flight requests, which is the graceful shutdown from A modern .NET backend (8.3).

Helm packages sets of manifests as reusable, configurable charts. `kubectl apply -f deployment.yaml` sends manifests to a cluster, and in practice pipelines or GitOps tools such as Argo CD and Flux apply them from a repository.

### Do you need Kubernetes?

Kubernetes is powerful and has a large operational cost: clusters to upgrade, networking to configure, and a lot of concepts for every team to learn. Many services don't need it. Managed container platforms such as Azure Container Apps, AWS App Runner and ECS on Fargate, and Google Cloud Run run containers with scaling, rollouts and health checks, and no cluster to manage. Many of them are built on Kubernetes underneath. Platform services such as Azure App Service run .NET apps with or without containers. Cloud basics (11.5) compares the options. Kubernetes earns its cost when an organization runs many services and has a platform team to run it.

### In short

A container image packages a program with everything it needs in content-addressed layers; containers are running instances of it. Store images in registries, and deploy by digest or never-reused tags. Write multi-stage Dockerfiles: build with the SDK image, run on a small runtime or chiseled image as non-root, and order steps so restores stay cached. Keep the build context clean with `.dockerignore`, or build images straight from `dotnet publish`. Configure containers through environment variables, keep data in volumes, log to standard output and set resource limits. Use Compose for local environments. Scan, rebuild and sign images, and never bake in secrets. Kubernetes reconciles declared deployments, services and probes across a cluster; simpler container platforms are often enough.

### What goes wrong

**Secrets in images.** A key copied in during the build is readable by anyone who pulls the image, even after a later step deletes it. Supply secrets at run time.

**Huge images.** Shipping the SDK, source code and build tools makes images slow to pull and full of vulnerabilities. Use multi-stage builds and small runtime images.

**Cache-busting Dockerfiles.** Copying all source before restoring re-downloads every package on each change. Copy project and lock files first.

**Mutable tags in production.** `latest` quietly changes, and two nodes run different code. Deploy by digest or unique tags.

**Running as root.** A compromised process gets root inside the container, and more if the container escapes. Run as a non-root user.

**Data in the container's own layer.** A database container is replaced and its data disappears. Use volumes.

**Missing or wrong probes.** Without readiness probes, traffic reaches pods before they can serve; with over-eager liveness probes, slow pods are restarted in a loop. Tune both.

**Kubernetes by default.** A small team spends its time running a cluster instead of building the product. Start with a managed container platform.

### Cost model

A container starts in well under a second when its image is already on the machine, because no operating system boots; a .NET app's own start-up then dominates. Pulling an image costs its compressed size over the network, so SDK-based images of several hundred megabytes cost far more than runtime images of around a hundred, and chiseled images less again. Cached layers make repeat builds take seconds. Containers themselves add almost no CPU overhead, since they are ordinary processes. Kubernetes adds control-plane and per-node system overhead, and above all the ongoing human cost of running and upgrading clusters.

### In other languages (optional)

**Every stack.** Containers make the language almost irrelevant to deployment: a Node.js, Python or Go service ships as an image exactly like a .NET one, which is much of their appeal.

**Node.js and Python.** Dockerfiles follow the same pattern: install dependencies from the lockfile in an early, cached step, copy source later, and run on a slim base image as a non-root user.

**Go and Rust.** Compile to a single static binary that can run in an almost empty image, often only a few megabytes. .NET Native AOT can come close.

**Other tools.** Podman runs the same images without a background daemon, and Buildah and BuildKit build them.

### Key terms

- **Container image**: A read-only package of a program's filesystem and settings, from which containers are started.
- **Docker**: The tool that popularized containers, used to build images and run containers.
- **Image layer**: A content-addressed set of file changes; images are stacks of layers.
- **Base image**: The image a Dockerfile starts from, such as the official ASP.NET Core runtime.
- **Container registry**: A service that stores and serves container images.
- **Image tag**: A movable, human-readable label for an image version, such as 1.4.2.
- **Image digest**: The hash of an image's content, which always identifies exactly the same image.
- **Dockerfile**: A file of instructions that builds a container image.
- **Build context**: The files sent to the image builder, filtered by .dockerignore.
- **Multi-stage build**: A Dockerfile that builds in one image and copies only the results into a smaller final image.
- **Distroless image**: A minimal base image with only what a program needs, without a shell or package manager.
- **Volume**: Storage managed by the container runtime that survives containers being replaced.
- **Docker Compose**: A tool for defining and running several containers together from one YAML file.
- **Container orchestration**: Automatically running, healing, scaling and updating containers across many machines.
- **Kubernetes**: The dominant container orchestrator, which reconciles declared desired state across a cluster.
- **Pod**: The smallest unit Kubernetes runs: one or more containers sharing a network address.
- **Kubernetes Deployment**: A Kubernetes object declaring how many replicas of a pod to run, and managing rolling updates.
- **Kubernetes Service**: A stable name and address for a set of pods, spreading traffic across the ready ones.
- **Kubernetes probe**: A readiness, liveness or start-up check that Kubernetes runs against each container.

### Where this leads

Building, scanning and deploying images automatically is in CI/CD pipelines (11.4). Where containers run in the cloud, and the platforms that run them for you, are in Cloud basics (11.5). Collecting logs, metrics and traces from containers is in Observability (11.6), and scaling services out is in Scaling and performance (12.4).

### Check yourself

1. Build the Dockerfile for an ASP.NET Core app you have, change one line of code and rebuild, and check which steps come from the cache.
2. Compare the sizes of the same app built on `aspnet:10.0` and on `aspnet:10.0-noble-chiseled`, then try to open a shell in each.
3. Publish the same app with `dotnet publish -t:PublishContainer` and run it without any Dockerfile.
4. Start the compose file's database and cache, stop and remove the containers, start them again, and check that the database's data survived.
5. Scan an image you use with Trivy, and decide which findings a rebuild on a newer base image would fix.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: A 3-minute Docker build for a one-line change

Every code change re-downloads all NuGet packages because the Dockerfile copies the source before restoring.

*Cache-friendly multi-stage Dockerfile*

```docker
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Packages.props Directory.Build.props ./
COPY src/Bookings.Api/Bookings.Api.csproj src/Bookings.Api/
RUN dotnet restore src/Bookings.Api/Bookings.Api.csproj        # cached until a csproj changes
COPY . .
RUN dotnet publish src/Bookings.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID                                                  # non-root
ENTRYPOINT ["dotnet", "Bookings.Api.dll"]
```

> **Lesson:** Order Dockerfile steps from least to most frequently changing, and ship only the runtime as a non-root user.

#### Example 2: A pod OOM-killed with plenty of free heap

A pod with a 512 MiB limit and 4 CPUs is killed repeatedly; GC heap never exceeds 300 MiB.

**What goes wrong.** Server GC creates a heap per core and the process also needs native memory; together they cross the limit.

*Size the GC for the container*

```yaml
env:
  - name: DOTNET_GCHeapHardLimitPercent
    value: "0x46"            # 70% of the container limit for the GC heap (hex)
resources:
  requests: { cpu: "500m", memory: "512Mi" }
  limits:   { memory: "512Mi" }
```

- Alternatively use workstation GC or DOTNET_GCHeapCount for small, many-core containers.

> **Lesson:** Leave headroom for non-GC memory, and tell the GC its budget inside small containers.

### Containers & Kubernetes for .NET

<p class="src">From <em>.NET Backend Deep Dive</em> · Containers & Kubernetes for .NET</p>

<a id="dn-154"></a>

#### A Dockerfile that isn't wrong

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files FIRST so restore is cached independently of source changes
COPY *.sln .
COPY src/Api/*.csproj src/Api/
COPY src/Domain/*.csproj src/Domain/
RUN dotnet restore

COPY . .
RUN dotnet publish src/Api/Api.csproj -c Release -o /app --no-restore

# Runtime stage - aspnet, not sdk (~110MB vs ~800MB)
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app
COPY --from=build /app .

# Non-root. The images ship an 'app' user (UID 1654).
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
```

What goes wrong most often: copying the whole source before `restore` (every code change re-downloads every package), shipping the SDK image to production, running as root, and hardcoding `ASPNETCORE_ENVIRONMENT=Development` (which turns on the developer exception page and leaks stack traces).

Alpine images are much smaller but use musl — check native dependencies, and note that globalization needs `icu-libs` or `InvariantGlobalization=true` (see Part 32.2).

<a id="dn-155"></a>

#### The GC/container interaction that surprises people

.NET reads the cgroup memory limit and sizes its heap against it — but **Server GC allocates per-core heaps**, so a pod with a generous CPU allocation and a small memory limit will hold far more memory than expected and get OOMKilled (Part 20.4).

```yaml
resources:
  requests: { memory: "256Mi", cpu: "250m" }
  limits:   { memory: "512Mi", cpu: "1000m" }
```

```xml
<!-- For small containers, Workstation GC often uses far less memory -->
<ServerGarbageCollection>false</ServerGarbageCollection>
<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
```

Or cap it explicitly: `DOTNET_GCHeapHardLimitPercent=0x4B` (75%), leaving headroom for non-GC memory (native, stacks, JIT).

**CPU limits and throttling:** a limit of `1000m` doesn't give you one dedicated core — it's a CFS quota. The thread pool sizes itself from the *visible* core count, so a 16-core node with a 1-core limit produces a thread pool sized for 16 threads that gets throttled hard. Symptom: periodic latency spikes with no GC and no slow queries. Use `DOTNET_PROCESSOR_COUNT` to align it.

### Deployment & DevOps

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Deployment & DevOps</p>

<p class="pd">From dotnet publish to a running production system: containers, orchestration, pipelines, release strategies, and the operational habits that keep it up.</p>

<a id="be-a215"></a>

#### Containerising a .NET application

``` text
# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# copy csproj first so restore is cached when only code changes
COPY ["src/Orders.Api/Orders.Api.csproj", "src/Orders.Api/"]
COPY ["Directory.Packages.props", "Directory.Build.props", "./"]
RUN dotnet restore "src/Orders.Api/Orders.Api.csproj"

COPY . .
RUN dotnet publish "src/Orders.Api/Orders.Api.csproj" -c Release -o /app --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Orders.Api.dll"]
```

- **Multi-stage** keeps the SDK (about 1 GB) out of the shipped image.
- **Copy csproj before source** so the restore layer is cached — this alone halves most build times.
- **Chiseled or Alpine base** images have no shell and no package manager, which shrinks both size and attack surface.
- **Run as non-root.** The Microsoft images define `$APP_UID` for exactly this.
- **Pin the tag** to a specific version, not `latest`, and scan images in CI (Trivy, Grype).

You can skip the Dockerfile entirely with `dotnet publish /t:PublishContainer`, which builds an OCI image directly from the SDK — convenient, and it produces sensible defaults.

> **\[Trap\]**
>
> **Common trap.** Containers get a memory limit, and .NET reads it — but Server GC will happily grow the heap toward that limit before collecting. If pods are being OOM-killed, set `DOTNET_GCHeapHardLimitPercent` or switch to Workstation GC for small containers.

###### Worked example

Layer caching is the difference between a 20-second build and a 4-minute one, and the ordering rules are not obvious. Here is the same Dockerfile, wrong and right.

``` text
# WRONG: any source change invalidates the restore layer
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .                                  # <- one changed .cs file busts everything below
RUN dotnet restore                        # 90 s, every single build
RUN dotnet publish -c Release -o /app
```

``` bash
# RIGHT: restore depends only on project files
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props nuget.config ./
COPY src/Orders.Api/Orders.Api.csproj          src/Orders.Api/
COPY src/Orders.Application/*.csproj           src/Orders.Application/
COPY src/Orders.Domain/*.csproj                src/Orders.Domain/
COPY src/Orders.Infrastructure/*.csproj        src/Orders.Infrastructure/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/Orders.Api/Orders.Api.csproj

COPY src/ src/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/Orders.Api/Orders.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=1 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orders.Api.dll"]
```

| Change                | Rebuild time, wrong Dockerfile | Right Dockerfile                    |
|-----------------------|--------------------------------|-------------------------------------|
| One line of C#        | ~4 min (full restore + build)  | ~25 s (build only)                  |
| Added a NuGet package | ~4 min                         | ~2 min (restore re-runs, correctly) |
| Nothing (rebuild)     | ~4 min                         | ~2 s (all cached)                   |

The `--mount=type=cache` for the NuGet folder is a BuildKit feature that persists the package cache across builds without putting it in a layer. On a CI runner with a warm cache it removes most of the restore time even when a package does change.

###### Image choice, and what you give up

| Base tag                    | Size    | Has                                    | Trade-off                                        |
|-----------------------------|---------|----------------------------------------|--------------------------------------------------|
| `aspnet:9.0`                | ~220 MB | Full Debian, shell, apt, ICU           | Largest attack surface; easiest to debug         |
| `aspnet:9.0-alpine`         | ~110 MB | musl libc, shell, apk                  | Some native libraries need glibc; test carefully |
| `aspnet:9.0-noble-chiseled` | ~110 MB | Nothing — no shell, no package manager | Cannot `exec` into it; use `kubectl debug`       |
| `...-chiseled-extra`        | ~120 MB | Adds ICU and tzdata                    | Use when you need globalization                  |
| `runtime-deps` + AOT        | ~15 MB  | Just the OS dependencies               | AOT constraints apply throughout                 |

``` text
# ICU: the single most common first container failure
# error: "Couldn't find a valid ICU package installed on the system"
# either use a -extra image, or if you genuinely do not need culture data:
<InvariantGlobalization>true</InvariantGlobalization>

# time zones: chiseled images have no tzdata either
# TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam") -> TimeZoneNotFoundException
# fix: use the -extra tag, or bundle tzdata explicitly
```

A related detail worth knowing: since .NET 8 the official images use IANA time zone IDs on Linux and Windows IDs on Windows, and `TimeZoneInfo` converts between them. Code that hardcodes `"W. Europe Standard Time"` works on Windows and throws in a Linux container — a very common porting failure when moving off IIS.

<a id="be-a216"></a>

#### Docker Compose for local development

``` csharp
services:
  api:
    build: { context: ., dockerfile: src/Orders.Api/Dockerfile }
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__Default: "Host=db;Database=orders;Username=app;Password=dev"
      ConnectionStrings__Redis: "cache:6379"
    ports: ["8080:8080"]
    depends_on:
      db:    { condition: service_healthy }
      cache: { condition: service_started }

  db:
    image: postgres:17
    environment: { POSTGRES_PASSWORD: dev, POSTGRES_DB: orders, POSTGRES_USER: app }
    volumes: ["pgdata:/var/lib/postgresql/data"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app"]
      interval: 5s
      retries: 10

  cache:
    image: redis:7-alpine

volumes: { pgdata: }
```

Note the double underscore in `ConnectionStrings__Default`: that is how .NET configuration maps environment variables onto nested JSON keys. It is the mechanism that lets the same image run in Compose, Kubernetes and App Service with no code change.

> **\[Note\]**
>
> **depends_on is not enough on its own.** It waits for the container to start, not for the database to accept connections. Use a healthcheck condition as above, and make the app retry its first connection anyway.

###### A dev loop that is actually fast

``` csharp
# compose.yaml - dependencies only. Your service runs on the HOST,
# under the debugger, with hot reload. This is the fast loop.
services:
  db:
    image: postgres:17
    environment: { POSTGRES_PASSWORD: dev, POSTGRES_DB: appdb }
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      retries: 10
  cache:
    image: redis:7-alpine
    ports: ["6379:6379"]
  mq:
    image: rabbitmq:3-management
    ports: ["5672:5672", "15672:15672"]
volumes: { pgdata: }
```

``` text
  THE TWO LOOPS, and choosing deliberately

  DEPENDENCIES IN COMPOSE, APP ON THE HOST
    dotnet watch run
    -> edit, save, reload in ~1 second
    -> full debugger, hot reload, breakpoints
    -> THE LOOP YOU WANT while writing code

  EVERYTHING IN COMPOSE
    docker compose up --build
    -> 30-90 seconds per change
    -> tests the container itself: the Dockerfile, the entrypoint,
       the non-root user, the health check
    -> run this BEFORE PUSHING, not while coding

  most teams use compose for the second and forget the first exists,
  then wonder why development feels slow.
```

| Compose gotcha                                  | Fix                                                          |
|-------------------------------------------------|--------------------------------------------------------------|
| `localhost` inside a container is the container | Use the service name; or `host.docker.internal` for the host |
| The app starts before the database is ready     | `depends_on` with `condition: service_healthy`               |
| Data lost on `down`                             | A named volume. `down -v` removes it deliberately            |
| Slow file access on macOS/Windows bind mounts   | Named volumes for dependencies; keep source on the host      |
| Port already in use                             | A previous stack still running — `compose down` first        |

Where Aspire fits: its AppHost replaces this file with typed C#, adds the wiring and the dashboard, and gives you the same fast loop by default. If you are already on modern .NET and the compose file has grown past a dozen services and environment variables, that is the point at which the swap starts paying for itself.

### Compute: VMs to Serverless

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Compute: VMs to Serverless</p>

<p class="pd">Where code runs, from a physical machine to a function that exists for 40 milliseconds — and how to choose without following fashion.</p>

<a id="be-g663"></a>

#### Bare metal, virtual machines and containers

``` text
  BARE METAL          VIRTUAL MACHINE           CONTAINER

  your app            your app                  your app
  ------------        ------------              ------------
  OS                  guest OS (full)           (shares host kernel)
  ------------        ------------              ------------
  hardware            hypervisor                container runtime
                      ------------              ------------
                      host OS                   host OS
                      hardware                  hardware

  isolation   strongest -----------------------------> weakest
  overhead    none        ~5-10% + GBs of RAM     ~0% + MBs
  boot time   minutes     ~30s                    ~100ms
  density     1 app       tens per host           hundreds per host
```

The key technical fact: **a container is not a lightweight VM.** It is an ordinary Linux process with a restricted view of the system. There is no guest kernel, no virtualised hardware, and no boot — which is why it starts in milliseconds and why kernel-level isolation is weaker than a VM's.

| Mechanism            | Provides                                                                                  |
|----------------------|-------------------------------------------------------------------------------------------|
| **Namespaces**       | The restricted *view*: its own PID numbering, network stack, mount table, hostname, users |
| **cgroups**          | The resource *limits*: CPU, memory, I/O, process count                                    |
| **Capabilities**     | Fine-grained root privileges, so a container can bind port 80 without full root           |
| **seccomp**          | Restricts which syscalls the process may make                                             |
| **Union filesystem** | Layered images, sharing common layers across containers                                   |

| Choose         | When                                                                                      |
|----------------|-------------------------------------------------------------------------------------------|
| **Bare metal** | Maximum and predictable performance; licensing tied to cores; very large databases        |
| **VMs**        | Strong isolation between tenants; a full OS is genuinely needed; lift-and-shift of legacy |
| **Containers** | The default for new application workloads; fast deploys, density, portability             |
| **Serverless** | Spiky or infrequent load; no capacity to manage; event-driven work                        |

<a id="be-g664"></a>

#### Images, layers and a production Dockerfile

``` text
  images are LAYERS, stacked and cached

    layer 4  COPY app files          <- changes every build
    layer 3  RUN restore packages    <- changes when dependencies change
    layer 2  RUN apt-get install     <- rarely changes
    layer 1  FROM base image         <- almost never changes

  a rebuild reuses every layer up to the first CHANGED one.
  -> order your Dockerfile from LEAST to MOST frequently changing.
  -> copying source before restoring dependencies invalidates the
     dependency cache on every single code change. This one mistake
     turns a 20-second build into a four-minute one.
```

``` text
# multi-stage: build with the full SDK, ship only the runtime
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. dependency manifests FIRST - this layer caches across code changes
COPY *.sln .
COPY src/MyApp/*.csproj src/MyApp/
RUN dotnet restore

# 2. then the source
COPY . .
RUN dotnet publish src/MyApp -c Release -o /app --no-restore

# 3. a minimal runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled AS final
WORKDIR /app
COPY --from=build /app .

# run as non-root
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# exec form, so the process is PID 1 and RECEIVES SIGTERM
ENTRYPOINT ["./MyApp"]
```

| Base image               | Size      | Trade                                                             |
|--------------------------|-----------|-------------------------------------------------------------------|
| `debian` / `ubuntu`      | ~80 MB+   | Full toolset; largest attack surface                              |
| `-slim`                  | ~30–80 MB | Reasonable default                                                |
| `alpine`                 | ~5 MB     | musl libc — can break glibc-dependent binaries and DNS edge cases |
| `-chiseled` / distroless | ~15–30 MB | No shell, no package manager. Excellent security; harder to debug |
| `scratch`                | 0         | Static binaries only                                              |

| Image practice               | Why                                                                        |
|------------------------------|----------------------------------------------------------------------------|
| Pin by digest, not `:latest` | `latest` makes builds irreproducible                                       |
| `.dockerignore`              | Keeps `.git`, `node_modules` and secrets out of the build context          |
| Never `COPY` secrets         | Layers are permanent; deleting in a later layer does not remove it         |
| Use build secrets            | `RUN --mount=type=secret` — never persisted into a layer                   |
| One process per container    | So the orchestrator can supervise and restart it                           |
| Scan images                  | Trivy, Grype — in CI, and again on a schedule for already-published images |

> **\[Trap\]**
>
> **Common trap.** A secret copied into an image and deleted in a later layer is still in the image, extractable by anyone who pulls it. Layers are immutable and additive; `RUN rm secret.txt` only hides it from the final filesystem view. Use `--mount=type=secret`, or inject secrets at runtime.

###### Build caching in CI, and why builds get slow

``` bash
# the problem: CI runners are ephemeral, so the layer cache is empty every run.
# a build that takes 20 seconds locally takes 4 minutes in CI, every time.

# BuildKit with a registry-backed cache
docker buildx build \
  --cache-from type=registry,ref=registry.example.com/myapp:buildcache \
  --cache-to   type=registry,ref=registry.example.com/myapp:buildcache,mode=max \
  --push -t registry.example.com/myapp:$SHA .

# cache mounts: persist a package cache ACROSS builds without baking it in
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore

RUN --mount=type=cache,target=/root/.npm \
    npm ci

# build secrets: available during the build, never written to a layer
RUN --mount=type=secret,id=nuget_pat \
    dotnet restore --source "https://pkgs.example.com/v3/index.json"
```

| Build slowness                  | Cause                                   | Fix                                              |
|---------------------------------|-----------------------------------------|--------------------------------------------------|
| Every layer rebuilds            | Source copied before dependency restore | Reorder: manifests, restore, then source         |
| Cache never hits in CI          | Ephemeral runner, no external cache     | Registry or GHA cache backend                    |
| Huge build context              | No `.dockerignore`                      | Exclude `.git`, `node_modules`, `bin`, `obj`     |
| Slow dependency restore         | No package cache                        | `--mount=type=cache`                             |
| Multi-arch builds doubling time | Emulated cross-build                    | Native runners per architecture, then a manifest |

Worth measuring rather than assuming: a four-minute build against a ten-minute deploy pipeline is 40% of your lead time. Teams frequently optimise test execution while the container build quietly costs more.

### Shipping it

<p class="src">From <em>Backend Engineering</em> · Worked Builds · Shipping it</p>

<p class="pd">The artefacts that turn working code into a running service: the container, the health endpoints, the observability wiring and the deployment manifest.</p>

<a id="be-w14"></a>

#### A production Dockerfile for .NET

``` text
# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# copy ONLY the project files first — this layer caches until dependencies change
COPY Directory.Packages.props Directory.Build.props ./
COPY src/Orders.Api/*.csproj      src/Orders.Api/
COPY src/Orders.Domain/*.csproj   src/Orders.Domain/
COPY src/Orders.Infra/*.csproj    src/Orders.Infra/
RUN dotnet restore src/Orders.Api/Orders.Api.csproj

# now the source; edits below here do not invalidate the restore layer
COPY src/ src/
RUN dotnet publish src/Orders.Api/Orders.Api.csproj \
    -c Release -o /app/publish \
    /p:UseAppHost=false /p:PublishReadyToRun=true

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final
# chiseled: no shell, no package manager, ~40 % smaller, far smaller attack surface

WORKDIR /app
COPY --from=build /app/publish .

# non-root: chiseled images already define uid 64198 as "app"
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_gcServer=1 \
    DOTNET_GCHeapHardLimitPercent=75
EXPOSE 8080

ENTRYPOINT ["dotnet", "Orders.Api.dll"]
```

> **\[Trap\]**
>
> **Common trap.** Copying the whole source before `restore` is the most common Dockerfile mistake: every one-character change re-downloads every package, turning a 20-second build into three minutes. And never `COPY . .` without a `.dockerignore` — `bin/`, `obj/`, `.git` and local `appsettings.Development.json` (with its secrets) all end up in the image.
