---
id: "11-2"
number: "11.2"
title: "Builds and dependencies"
part: 11
part_title: "Shipping software"
words: 5386
minutes: 23
---

# 11.2 Builds and dependencies

> **Quick look.** How .NET builds work and how to keep dependencies under control.
>
> - The SDK runs MSBuild over short SDK-style projects; share settings in Directory.Build.props and pin the SDK in global.json.
> - Treat warnings as errors and enable recommended analyzers.
> - NuGet installs one version per package per app; Central Package Management keeps versions in one file.
> - Commit lock files, restore in locked mode, avoid floating versions.
> - Audit transitive dependencies, automate updates, produce an SBOM, and use package source mapping against dependency confusion.
> - Build once, deploy many; tag artifacts so you always know what is running.


### The big picture

Between the code in a repository and a program running in production sits the build. It restores the packages the code depends on, compiles it, runs analyzers, and produces artifacts that can be tested and deployed. When builds are fast, repeatable and identical everywhere, nobody thinks about them. When they aren't, "it works on my machine" becomes a daily problem: a package version drifts, an SDK differs between laptops, or a dependency pulls in a vulnerable library that nobody chose.

This chapter covers how .NET builds work: the SDK, MSBuild and project files, shared settings for a whole repository, and the choices when publishing. Most of it is about dependencies, the part that causes the most trouble. It covers how NuGet chooses a version when two packages disagree, pinning everything with lock files, managing versions centrally, and scanning for known vulnerabilities. It also covers defending against attacks that slip malicious packages into a build. The example models NuGet's and npm's version rules and runs them, so you can see why the two ecosystems behave differently. The configuration files in the chapter are real, reusable files, checked to be well-formed.

### Before you start

**Builds on:** 2.2 Compilers, interpreters and JIT, 2.3 Types, 8.4 A Node and TypeScript backend, 10.4 Common web vulnerabilities, 9.2 Modern JavaScript.

**Ideas explained later:**

- **Production** (11.4): The environment that real users and real data depend on, as opposed to development, test or staging.
- **Performance** (12.4): How quickly a system does its work at a given load.
- **Azure** (11.5): Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **Container image** (11.3): A read-only package of a program's filesystem and settings, from which containers are started.
- **Docker** (11.3): The tool that popularized containers, used to build images and run containers.

**Tools and technologies named here:**

- **Deploy** (11.4): To install a new version of software where it will run, such as on production servers.
- **CI** (11.4): Continuous integration: automatically building and testing every change as soon as it is pushed.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. Two packages need Newtonsoft.Json, one at 12.0.1 or later and one at 13.0.1 or later. Which version does NuGet pick, if 13.0.3 is available?**

> 13.0.1: NuGet picks the lowest version that satisfies every requirement, not the newest. It installs one version of each package for the whole application.

**2. What is a transitive dependency?**

> A package you didn't reference yourself, pulled in because one of your dependencies needs it. Most of an application's packages are transitive, and they can carry vulnerabilities too.

**3. What does Central Package Management do?**

> It moves every package version into one `Directory.Packages.props` file, so projects reference packages without versions and the whole repository uses the same version of each package.

**4. What is a dependency confusion attack?**

> Publishing a package to a public feed with the same name as a company's internal package, so builds that search both feeds pull the attacker's version. Package source mapping prevents it by fixing which feed each package name may come from.

**5. What does global.json control?**

> Which .NET SDK version builds the repository, so every developer and pipeline uses the same one, with a rule for how far it may roll forward to newer patches.

**6. What is the difference between a framework-dependent and a self-contained publish?**

> A framework-dependent publish needs the .NET runtime installed on the target machine, and is small. A self-contained publish includes the runtime, so it runs anywhere with the matching operating system and architecture, and is larger.

**7. How do you find vulnerable packages in a .NET solution, including transitive ones?**

> Restore warns automatically through NuGet audit, and `dotnet list package --vulnerable --include-transitive` lists them. Automated tools such as Dependabot open pull requests with fixes.

### What a build does

A **build** turns source code into artifacts: assemblies, packages, container images, or a published folder ready to deploy. For a .NET project, it restores the NuGet packages the project needs, runs source generators, compiles C# into assemblies with the compiler from Compilers, interpreters and JIT (2.2), runs analyzers, and copies content and configuration files into the output.

Builds are incremental: tools skip steps whose inputs haven't changed, which is why a second build is much faster than the first. That makes stale output possible when the tools misjudge what changed, and "clean and rebuild" the classic remedy. In a pipeline, builds start from a clean checkout every time, which is one reason pipelines catch problems that laptops don't.

### The .NET SDK and MSBuild

The **.NET SDK** is the set of tools for building .NET code: the `dotnet` command, the compilers, **MSBuild**, templates and the libraries needed to target each version of .NET. The runtime alone can run apps; the SDK is needed to build them.

MSBuild is the build engine underneath `dotnet build`. It reads a **project file**, the `.csproj`, which is XML made of properties, such as the target framework, items, such as package references and files, and targets, the steps that run. Modern project files are short, because the SDK named at the top brings sensible defaults: `Microsoft.NET.Sdk` for libraries and console apps, and `Microsoft.NET.Sdk.Web` for ASP.NET Core. All `.cs` files under the project folder are included automatically.

The **target framework**, such as `net10.0`, says which .NET version the project builds for, and so which APIs it may use. A library can target several frameworks at once. A solution file, `.sln`, or the newer, simpler `.slnx` format, groups projects so they build together.

Settings that every project should share belong in a `Directory.Build.props` file at the root of the repository. MSBuild finds it automatically and applies it to every project below it. A `global.json` file pins the SDK version, so every developer and pipeline builds with the same tools:

```xml
<Project>
  <!-- Settings every project in the repository inherits -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <NuGetAuditMode>all</NuGetAuditMode>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

`TreatWarningsAsErrors` makes warnings, including nullable reference warnings from Types (2.3), fail the build, so they get fixed rather than accumulating in their thousands. `AnalysisLevel` turns on the recommended set of Roslyn analyzers, which check code for bugs, performance problems and security mistakes as part of compiling it. This **static analysis**, examining code without running it, catches whole classes of problem before tests do. An `.editorconfig` file sets code style rules that the same analyzers enforce.

### Packages and versions

Almost every project depends on packages. **NuGet** is .NET's package system: packages are zip files containing assemblies and metadata, published to feeds such as nuget.org or a private feed in Azure Artifacts or GitHub Packages. A project lists what it needs with `PackageReference` items.

Each of those packages has dependencies of its own, which have their own, and so on. A package you didn't reference yourself, but that arrives because something you use needs it, is a **transitive dependency**. A typical web app references a dozen packages and actually loads well over a hundred.

When several packages need the same dependency at different versions, something must decide which version to use. NuGet installs only one version of each package for the whole application, because an application can normally load only one version of an assembly. It decides with a few rules. Where a dependency's range allows several versions, it picks the lowest version that satisfies every package asking for it, rather than the newest, which makes restores predictable. A version you reference directly always wins. If that direct reference is lower than something else needs, NuGet reports warning NU1605, a downgrade, which the .NET SDK treats as an error by default. npm, from A Node and TypeScript backend (8.4), takes a different approach: each package gets the highest version matching its own range, and several copies of the same library can be installed side by side.

### Seeing it for real

This script is a small model of both systems' rules. It isn't a full resolver, but it follows each system's documented behaviour for these cases, so you can see how the same dependencies resolve differently.

```typescript
// resolve.ts: run with: node resolve.ts   (a simplified model of how NuGet and npm choose versions)
type Version = [number, number, number];
const parse = (v: string) => v.split(".").map(Number) as Version;
const cmp = (a: string, b: string) => { const x = parse(a), y = parse(b); return x[0] - y[0] || x[1] - y[1] || x[2] - y[2]; };

// The feed: every version of every package, and what each version depends on (">= x" is NuGet's default range)
const feed: Record<string, Record<string, Record<string, string>>> = {
  "Newtonsoft.Json": { "12.0.1": {}, "12.0.3": {}, "13.0.1": {}, "13.0.3": {} },
  "Flights.Client": { "1.0.0": { "Newtonsoft.Json": ">= 12.0.1" } },
  "Payments.Sdk": { "2.0.0": { "Newtonsoft.Json": ">= 13.0.1" } },
};
const satisfies = (v: string, range: string) => range.startsWith(">= ") ? cmp(v, range.slice(3)) >= 0
  : range.startsWith("^") ? parse(v)[0] === parse(range.slice(1))[0] && cmp(v, range.slice(1)) >= 0 : v === range;
const versions = (name: string) => Object.keys(feed[name]!).sort(cmp);

// NuGet: one version per package for the whole app. A direct reference wins outright;
// otherwise the lowest version that satisfies every range that asks for the package.
function nuget(direct: Record<string, string>) {
  const ranges: Record<string, string[]> = {};
  for (const [name, version] of Object.entries(direct))
    for (const [dep, range] of Object.entries(feed[name]?.[version] ?? {})) (ranges[dep] ??= []).push(`${name} needs ${range}`);
  const chosen: Record<string, string> = { ...direct }, warnings: string[] = [];
  for (const [dep, asks] of Object.entries(ranges)) {
    const needs = asks.map(a => a.split(" needs ")[1]!);
    if (direct[dep]) {
      for (const a of asks) if (!satisfies(direct[dep]!, a.split(" needs ")[1]!)) warnings.push(`NU1605: detected package downgrade: ${dep} from ${a.split(" needs >= ")[1]} to ${direct[dep]} (${a})`);
      continue;
    }
    chosen[dep] = versions(dep).find(v => needs.every(r => satisfies(v, r)))!;   // lowest applicable version
  }
  return { chosen, warnings };
}

// npm: each dependent gets the highest version matching its own range; different ranges may install several copies
function npm(ranges: Record<string, string>) {
  return Object.fromEntries(Object.entries(ranges).map(([who, range]) => [who, versions("Newtonsoft.Json").filter(v => satisfies(v, range)).at(-1)]));
}

console.log("NuGet, two packages need Newtonsoft.Json:", nuget({ "Flights.Client": "1.0.0", "Payments.Sdk": "2.0.0" }));
// { chosen: { ..., 'Newtonsoft.Json': '13.0.1' } }: the lowest version both accept, not the newest
console.log("NuGet, the app also references 12.0.3 directly:", nuget({ "Flights.Client": "1.0.0", "Payments.Sdk": "2.0.0", "Newtonsoft.Json": "12.0.3" }));
// the direct reference wins, and NuGet warns that Payments.Sdk now gets a version it didn't ask for
console.log("npm, the same two packages with ^ ranges:", npm({ "flights-client": "^12.0.1", "payments-sdk": "^13.0.1" }));
// { 'flights-client': '12.0.3', 'payments-sdk': '13.0.3' }: two copies, the highest match for each
```

Its output:

```text
NuGet, two packages need Newtonsoft.Json: {
  chosen: { 'Flights.Client': '1.0.0', 'Payments.Sdk': '2.0.0', 'Newtonsoft.Json': '13.0.1' },
  warnings: []
}
NuGet, the app also references 12.0.3 directly: {
  chosen: { 'Flights.Client': '1.0.0', 'Payments.Sdk': '2.0.0', 'Newtonsoft.Json': '12.0.3' },
  warnings: [ 'NU1605: detected package downgrade: Newtonsoft.Json from 13.0.1 to 12.0.3 (Payments.Sdk needs >= 13.0.1)' ]
}
npm, the same two packages with ^ ranges: { 'flights-client': '12.0.3', 'payments-sdk': '13.0.3' }
```

Neither is wrong. NuGet's single version per package keeps an application consistent, and warns when a choice breaks a package's stated needs. npm's copies avoid conflicts, at the cost of larger installs and occasional subtle bugs when two copies of a library meet.

### Keeping versions under control

Three NuGet features keep a repository's dependencies consistent and repeatable.

**Central Package Management** moves every package version into one `Directory.Packages.props` file at the root. Project files then reference packages by name only. Every project uses the same version of each package, and an upgrade is one line in one file. With transitive pinning turned on, the file can also fix the version of a transitive dependency, which is how you force a vulnerable one up to a patched version:

```xml
<Project>
  <!-- Central Package Management: every package version in one place -->
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.0.0" />
  </ItemGroup>
</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <!-- No versions here: they come from Directory.Packages.props -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <ProjectReference Include="..\Bookings.Domain\Bookings.Domain.csproj" />
  </ItemGroup>
</Project>
```

A package lock file, `packages.lock.json`, records the exact version of every package, transitive ones included, like the lockfile from A Node and TypeScript backend (8.4). It is turned on with `RestorePackagesWithLockFile`, as in the `Directory.Build.props` above. Commit it, and restore in pipelines with `dotnet restore --locked-mode`, which fails if the lock file and the project disagree, rather than silently picking something new.

Avoid floating versions such as `10.*`, which resolve to whatever is newest at restore time. They make two builds of the same commit differ.

### Vulnerable dependencies

A dependency with a known vulnerability is a vulnerability in your application, as Common web vulnerabilities (10.4) described. Restore checks packages against a database of known vulnerabilities through NuGet audit, and warns, with warnings NU1901 to NU1904 by severity. `NuGetAuditMode` set to `all` includes transitive packages. `dotnet list package --vulnerable --include-transitive` lists affected packages on demand, and `dotnet list package --outdated` shows available updates; recent SDKs also offer these as `dotnet package list`.

Automated tools make updating routine. Dependabot on GitHub and Renovate on any host open pull requests when new versions appear, and the pipeline's tests show whether they are safe to merge. A **software bill of materials** (SBOM) is a machine-readable list of every component in a build and its version. Customers and regulators increasingly ask for one, and it answers "are we affected?" in minutes when the next Log4Shell appears. Tools such as Microsoft's sbom-tool generate one during the build.

### Attacks on the build

The build is an attack target in its own right, the supply chain attack from A Node and TypeScript backend (8.4). One specific form is **dependency confusion**. In 2021, a researcher showed he could get code running inside dozens of large companies by publishing public packages with the same names as their internal packages. Build tools configured to search both public and private feeds preferred the public one, because it had a higher version number.

**Package source mapping** prevents it. The `nuget.config` file maps package name patterns to the feeds they may come from, so names starting with `Airline.` can only ever be restored from the internal feed:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="airline" value="https://pkgs.dev.azure.com/airline/_packaging/internal/nuget/v3/index.json" />
  </packageSources>
  <!-- Package source mapping: internal names can only ever come from the internal feed -->
  <packageSourceMapping>
    <packageSource key="airline">
      <package pattern="Airline.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Other defences apply to every ecosystem: keep dependencies few, review what new packages bring in, require signed packages where available, pin the SDK and tools, and treat build machines and their credentials as production systems.

### Publishing

`dotnet publish` produces the files to deploy, with several choices. A framework-dependent publish, the default, relies on the .NET runtime being installed on the target, and is small. A **self-contained deployment** includes the runtime, so it runs on any machine with the matching operating system and processor architecture, named by a runtime identifier such as `linux-x64`. On top of that, single-file publishing bundles everything into one executable. Trimming removes unused code. ReadyToRun precompiles for faster start-up, and Native AOT compiles to a native executable with no JIT at all, from Compilers, interpreters and JIT (2.2). `dotnet publish -t:PublishContainer` builds a container image directly, as Containers and Docker (11.3) describes.

### Repeatable builds

A build should produce the same result from the same commit, whoever runs it and wherever. The .NET compiler is deterministic by default. `ContinuousIntegrationBuild`, set in the example when the pipeline defines `CI`, normalizes file paths embedded in the output, so builds on different machines match. Pinning the SDK with `global.json`, restoring with lock files and avoiding floating versions remove the other sources of drift. Source Link embeds the repository and commit in the assemblies' debug information, so a debugger can fetch the exact source of any production build.

The principle behind all of this is build once, deploy many. The pipeline builds and tests one artifact per commit, and that same artifact is promoted from test to staging to production, as CI/CD pipelines (11.4) describes. Rebuilding for each environment means production runs something that was never tested.

### In short

A build restores packages, compiles, analyzes and produces artifacts. The .NET SDK runs MSBuild over short SDK-style project files; share settings in `Directory.Build.props`, pin the SDK in `global.json`, and treat warnings as errors with recommended analyzers. NuGet installs one version of each package per application. It picks the lowest version satisfying every range, lets direct references win, and flags downgrades as NU1605. Centralize versions with Central Package Management, pin everything with committed lock files and locked-mode restores, and avoid floating versions. Audit dependencies, including transitive ones, automate updates and produce an SBOM. Prevent dependency confusion with package source mapping. Publish framework-dependent or self-contained, and build once, deploy many.

### What goes wrong

**Different SDKs on different machines.** A newer SDK on one laptop builds code that fails in the pipeline. Pin the SDK with `global.json`.

**Versions scattered across projects.** Five projects reference five versions of the same package, and bugs appear only in some. Use Central Package Management.

**Unpinned restores.** A new transitive version appears overnight, and a build of an old commit behaves differently. Commit lock files and restore in locked mode.

**Ignored warnings.** Thousands of warnings hide the few that matter. Treat warnings as errors, and fix or explicitly suppress each one.

**Vulnerable transitive packages.** Nobody chose the vulnerable library, so nobody updates it. Audit transitive dependencies and pin patched versions centrally.

**Public and private feeds mixed freely.** An attacker's public package shadows an internal one. Use package source mapping.

**Rebuilding for each environment.** Production runs a binary that was never tested. Build once and promote the same artifact.

### Cost model

A clean restore downloads every package once, then serves them from the local package cache; pipelines cache that folder between runs to save minutes. Compiling a medium-sized solution takes tens of seconds from clean, and incremental builds a few seconds. Analyzers add some compile time, typically 10 to 30%, which is cheap for the bugs they catch. Every direct dependency brings its transitive ones, each adding restore time, download size, audit work and attack surface. Self-contained publishes add the runtime, tens of megabytes, and trimming or Native AOT cut that back at the cost of longer builds and some reflection-heavy libraries not working.

### In other languages (optional)

**JavaScript and TypeScript.** npm, pnpm and Yarn with `package.json` and lockfiles, as A Node and TypeScript backend (8.4) described. Bundlers such as Vite, from Modern JavaScript (9.2), do the building. `npm audit` checks for vulnerabilities.

**Java.** Maven and Gradle resolve dependencies from Maven Central. Maven picks the nearest declaration in the dependency tree, a different rule again.

**Python.** pip with requirements files, or Poetry and uv with lockfiles; `pip-audit` scans for vulnerabilities.

**Go and Rust.** Go modules use minimal version selection, close in spirit to NuGet's lowest applicable version. Cargo, Rust's tool, allows several major versions of one crate side by side, like npm.

### Key terms

- **Build**: Turning source code into artifacts, by restoring packages, compiling, analyzing and packaging.
- **SDK**: Software development kit: the tools needed to build for a platform. The .NET SDK includes the dotnet command, compilers and MSBuild.
- **MSBuild**: The build engine that reads .NET project files and runs their build steps.
- **Project file**: The XML file, such as a .csproj, describing a project's properties, packages, references and build steps.
- **Target framework**: The .NET version a project builds for, such as net10.0, which decides the APIs it may use.
- **Static analysis**: Examining code for bugs and bad patterns without running it, as analyzers do during a build.
- **NuGet**: .NET's package system, with feeds such as nuget.org and private feeds.
- **Transitive dependency**: A package included because one of your dependencies needs it, not because you referenced it.
- **Central Package Management**: Keeping every package version for a repository in one Directory.Packages.props file.
- **Software bill of materials**: A machine-readable list of every component and version in a build. Often shortened to SBOM.
- **Dependency confusion**: Getting a malicious public package installed in place of a private package with the same name.
- **Package source mapping**: Configuration that fixes which feed each package name may be restored from.
- **Self-contained deployment**: A published app that includes the .NET runtime, so it needs nothing installed on the target.

### Where this leads

Packaging the published app into a container image is in Containers and Docker (11.3). Running restore, build, tests and audits on every commit, and promoting one artifact through environments, is in CI/CD pipelines (11.4).

### Check yourself

1. In `resolve.ts`, add a third package that needs Newtonsoft.Json 12.0.3 exactly, and predict what the NuGet model reports before running it.
2. Add `Directory.Build.props` with `TreatWarningsAsErrors` to a solution you work on, and count the warnings that now fail the build.
3. Convert a solution with scattered package versions to Central Package Management, and check that `dotnet restore` still succeeds.
4. Turn on lock files for one project, commit `packages.lock.json`, then change a package version and see `dotnet restore --locked-mode` fail.
5. Run `dotnet list package --vulnerable --include-transitive` on a real solution, and decide what to do about each result.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Package versions drifting across 30 projects

Different projects reference three versions of the same logging package, causing NU1605 downgrade warnings and runtime surprises.

*Central Package Management*

```markup
<!-- Directory.Packages.props at the repo root -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Serilog.AspNetCore" Version="9.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.0" />
  </ItemGroup>
</Project>
<!-- projects then use <PackageReference Include="Serilog.AspNetCore" /> with no version -->
```

> **Lesson:** One file for versions, lock files committed and locked restores in CI make builds identical everywhere.

#### Example 2: Warnings that hid a real bug

A solution has 2,300 warnings. One of them was a nullable warning on the line that crashed in production.

*Ratchet, don't flip*

```markup
<!-- Directory.Build.props -->
<PropertyGroup>
  <Nullable>enable</Nullable>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <!-- start with the rules that matter, then widen -->
  <WarningsAsErrors>CS8602;CS8604;CA1849;CA2016;CA2007</WarningsAsErrors>
</PropertyGroup>
<!-- new projects get <TreatWarningsAsErrors>true</TreatWarningsAsErrors> from day one -->
```

> **Lesson:** Warnings ignored for years hide the one that matters. Promote important rules to errors step by step.

### Deployment & DevOps

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Deployment & DevOps</p>

<p class="pd">From dotnet publish to a running production system: containers, orchestration, pipelines, release strategies, and the operational habits that keep it up.</p>

<a id="be-a214"></a>

#### Build and publish options

``` bash
dotnet publish -c Release -o out
dotnet publish -c Release -r linux-x64 --self-contained true
dotnet publish -c Release -r linux-x64 -p:PublishAot=true
```

| Mode                | Needs runtime installed? | Size      | Startup      | Trade-off                                             |
|---------------------|--------------------------|-----------|--------------|-------------------------------------------------------|
| Framework-dependent | Yes                      | ~1–5 MB   | Normal       | Smallest artifact; the default for containers         |
| Self-contained      | No                       | ~60–80 MB | Normal       | Runs anywhere; you own patching the runtime           |
| Trimmed             | No                       | ~20–30 MB | Normal       | Reflection can break — test thoroughly                |
| ReadyToRun          | Either                   | Larger    | Faster       | Pre-JIT; good for short-lived processes               |
| Native AOT          | No                       | ~10–15 MB | Milliseconds | No reflection, no runtime codegen, no dynamic plugins |

Native AOT is transformative for serverless and CLI tools, where cold start dominates. For a long-running web API the JIT eventually produces better code than AOT, so the gain is startup and memory, not throughput. Minimal APIs and System.Text.Json source generation are AOT-compatible; EF Core and most DI-heavy reflection is not fully there yet.

###### The four publish modes, and what each costs

| Mode                    | Output size | Startup         | Needs runtime installed | Trade                                               |
|-------------------------|-------------|-----------------|-------------------------|-----------------------------------------------------|
| **Framework-dependent** | ~1–5 MB     | Baseline        | **Yes**                 | The default. Smallest artifact, shared runtime      |
| **Self-contained**      | ~60–80 MB   | Baseline        | No                      | Runs anywhere; you patch the runtime yourself       |
| **+ ReadyToRun**        | +30–50%     | **2–3× faster** | Depends                 | Precompiled IL, JIT still available for hot paths   |
| **+ Trimmed**           | ~15–30 MB   | Faster          | No                      | Unused code removed — **reflection can break**      |
| **Native AOT**          | ~5–15 MB    | **~10× faster** | No                      | No JIT, limited reflection, some libraries unusable |

``` xml
<PropertyGroup>
  <PublishReadyToRun>true</PublishReadyToRun>      <!-- safe, broadly useful -->
  <PublishTrimmed>true</PublishTrimmed>            <!-- test thoroughly -->
  <TrimMode>full</TrimMode>
  <PublishAot>true</PublishAot>                    <!-- biggest gain, biggest cost -->
  <InvariantGlobalization>true</InvariantGlobalization>  <!-- see the warning below -->
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>    <!-- warns at BUILD time -->
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
</PropertyGroup>
```

``` text
  WHEN EACH IS WORTH IT

  framework-dependent + ReadyToRun
    the sensible default for a long-running web service in a container.
    startup matters at deploy and scale-out; the artifact stays small.

  self-contained
    when you cannot control what is installed on the host, or you need
    a runtime version the host does not have.

  trimmed
    when image size genuinely matters. Enable the analyzers, and test
    every reflection-dependent path - serialisation, DI scanning,
    EF Core model building are the usual casualties.

  Native AOT
    serverless functions, CLI tools, sidecars, and anything where cold
    start dominates. NOT for a typical ASP.NET Core app using EF Core -
    the ecosystem support is still partial.
```

> **\[Trap\]**
>
> **Common trap.** `InvariantGlobalization` is frequently switched on to shrink an image, and it silently changes behaviour rather than failing: every culture becomes invariant, culture-aware string comparison degrades to ordinal, `CultureInfo("nl-NL")` may throw, and currency and date formatting is wrong for every locale. Fine for a machine-to-machine API that formats nothing for humans; a bug for anything that displays localised text. Decide it consciously.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a209"></a>

#### Artifact, package and version management

How code becomes a versioned, promotable thing. Getting this wrong produces the two questions nobody can answer during an incident: *what is actually running*, and *where did it come from*.

1.  **Build once.** One artifact, produced from one commit, promoted unchanged through every environment. Rebuilding per environment means production runs a binary nothing tested.
2.  **Version from the repository.** MinVer or GitVersion derives the version from tags and commit height, so a version can never be forgotten or duplicated.
3.  **Label the artifact with its provenance.** Commit SHA, build number, and build time, as image labels and as assembly metadata.
4.  **Expose it at runtime.** An endpoint or a startup log line that states the exact version, so ‘which build is this?’ is answered in seconds.
5.  **Promote by reference, not rebuild.** Retag or reference the same digest; never `latest`.
6.  **Keep a retention policy.** Enough history to roll back several releases, not every build since 2021.

``` text
<!-- MinVer: version comes from git tags, nothing to maintain by hand -->
<ItemGroup>
  <PackageReference Include="MinVer" Version="6.0.0" PrivateAssets="all" />
</ItemGroup>
<PropertyGroup>
  <MinVerTagPrefix>v</MinVerTagPrefix>
  <MinVerDefaultPreReleaseIdentifiers>preview.0</MinVerDefaultPreReleaseIdentifiers>
</PropertyGroup>

<!-- tag v1.14.2      -> 1.14.2
     3 commits later  -> 1.14.3-preview.0.3
     no tag at all    -> 0.0.0-alpha.0.N  -->
```

``` csharp
// make the running version discoverable - one of the highest value-per-line features there is
app.MapGet("/version", () => Results.Ok(new
{
    version   = ThisAssembly.AssemblyInformationalVersion,   // "1.14.2+3f9a1c2"
    commit    = ThisAssembly.GitCommitId,
    builtAt   = BuildMetadata.TimestampUtc,
    framework = RuntimeInformation.FrameworkDescription,
    machine   = Environment.MachineName
})).AllowAnonymous().WithTags("Diagnostics");
```

| Versioning what          | Scheme                          | Rule                                                                        |
|--------------------------|---------------------------------|-----------------------------------------------------------------------------|
| Internal NuGet library   | SemVer                          | A major bump for any breaking change; consumers depend on this being honest |
| Service container image  | SemVer + commit SHA             | The SHA is what you actually debug with                                     |
| HTTP API                 | Independent of the build        | `v2` changes when the contract changes, not when the code does              |
| Message / event contract | Version in the type or a header | Never mutate a published event shape                                        |
| Database schema          | Migration sequence number       | Monotonic, and correlated to app versions in the release notes              |

> **\[Trap\]**
>
> **Common trap.** Assembly version, file version and informational version are three different fields, and only the last one can carry pre-release and metadata suffixes. Tools read different ones, which is why `1.14.2-preview.3+3f9a1c2` can appear as `1.14.2.0` in one place and the full string in another. Log the informational version; it is the only one with the whole story.

###### Reproducible builds, and the feed layout

``` text
<!-- Directory.Packages.props - one version per package, solution-wide -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Serilog.AspNetCore" Version="8.0.3" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
  </ItemGroup>
</Project>
<!-- projects then reference WITHOUT a version. This eliminates the most
     common solution-level bug: two projects on two versions of the same
     package, resolving to whichever happens to win at runtime. -->
```

``` bash
# reproducibility: the two commands that matter in CI
dotnet restore --locked-mode          # FAILS if the lock file would change
dotnet build --no-restore -c Release  # builds exactly what was restored

# enable lock files once, per project or in Directory.Build.props
<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>

# and make the build deterministic, so the same commit produces
# byte-identical output
<Deterministic>true</Deterministic>
<ContinuousIntegrationBuild Condition="'$(CI)'=='true'">true</ContinuousIntegrationBuild>
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
```

| Versioning decision         | Guidance                                                                            |
|-----------------------------|-------------------------------------------------------------------------------------|
| Floating versions (`8.0.*`) | **Avoid.** Builds stop being reproducible                                           |
| Version source              | Derive from git — MinVer or Nerdbank.GitVersioning. No manual edits                 |
| Pre-release packages        | A separate feed or a clear suffix, never mixed with stable                          |
| Internal package versioning | Semver, and treat a major bump as a breaking API change                             |
| Retention on the feed       | Keep every version ever deployed. You will need to reproduce an old build           |
| Upstream proxying           | Mirror nuget.org through your feed, so an upstream deletion cannot break your build |

The rule that ties this together: **the artifact is built once and promoted**. A package or container built for production must be byte-identical to the one that passed testing — which means configuration is external, the version comes from the commit, and no environment triggers a rebuild. If any environment rebuilds, you tested something other than what you shipped.
