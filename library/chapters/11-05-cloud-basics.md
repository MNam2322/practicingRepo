---
id: "11-5"
number: "11.5"
title: "Cloud basics"
part: 11
part_title: "Shipping software"
words: 12791
minutes: 56
---

# 11.5 Cloud basics

> **Quick look.** Renting computing on demand: services, regions, identity, infrastructure as code and cost.
>
> - IaaS, PaaS, SaaS and serverless; pick the least-control compute that works.
> - The provider secures the cloud; you secure data, identities, configuration and code.
> - Spread across availability zones; keep services on private endpoints in virtual networks.
> - Identity is the boundary: narrow role assignments and managed identities between services.
> - Define everything as code (Bicep, Terraform); the plan output is the review.
> - Serverless still needs idempotency and has cold starts; API gateways cap timeouts (often 29 s) and payload size.
> - Block public access on object storage and use short-lived presigned URLs.
> - Tag everything, set budgets, watch egress, and track cost per request.


### The big picture

Not long ago, running a new service meant ordering servers, waiting weeks for them to arrive, and installing and patching them yourself, then paying for them whether they were busy or idle. The cloud turned all of that into an API call. A database, a hundred servers, or a global content network can exist a few minutes after you ask for them, and disappear when you stop paying. Most new systems, airlines' included, now run on Microsoft Azure, Amazon Web Services or Google Cloud.

The cloud doesn't remove the need to understand the machine; everything in Parts 1 to 10 still happens, on someone else's hardware. What changes is the set of choices and responsibilities. This chapter covers what the cloud offers, from raw virtual machines to fully managed services, and which security jobs stay yours. It covers how providers are laid out in regions and zones, and the compute, data and networking building blocks. It then covers identity and least privilege, defining infrastructure as code, and cost, which works nothing like buying servers. The example is a Bicep file that defines the bookings API's Azure resources, compiled with the Bicep compiler. The chapter also shows a quiet mistake the compiler caught.

### Before you start

**Builds on:** 6.1 Layers and packets, 7.6 Non-relational stores, 11.3 Containers and Docker, 1.6 Storage and files, 8.7 Background work and queues, 6.6 HTTP.

**Ideas explained later:**

- **Reliability** (12.5): A system's ability to keep serving its users correctly over time despite failures in its parts.
- **Observability** (11.6): Being able to explain a system's behaviour, including new questions, from the signals it emits.
- **Performance** (12.4): How quickly a system does its work at a given load.

### Skip check

Answer each question in your head, then read the answer under it. If you got all of them, you can skip this chapter.

**1. What is the difference between IaaS, PaaS and SaaS?**

> IaaS rents virtual machines, disks and networks, and you manage everything from the operating system up. PaaS runs your code or data on a managed platform, such as App Service or a managed database. SaaS is finished software you simply use, such as Microsoft 365.

**2. What does the shared responsibility model say?**

> The provider secures the cloud itself: buildings, hardware, networks and the platforms it manages. You secure what you put in it: your data, identities, access rules, configuration and code. The split moves towards the provider as you move from IaaS to SaaS.

**3. What is the difference between a region and an availability zone?**

> A region is a geographic area with its own data centres, such as West Europe. Availability zones are physically separate data centres within one region, with independent power, cooling and networking. Spreading across zones survives a data centre failure.

**4. What does infrastructure as code give you?**

> Infrastructure defined in reviewed, versioned files that tools apply repeatably. Environments match, changes are reviewed like code, deployments can be previewed first, and a lost environment can be recreated.

**5. What is serverless computing?**

> Running code without managing servers or even instances: the platform runs functions or containers when requests or events arrive, scales them automatically, including to zero, and bills for actual use.

**6. Why can moving data between regions or out of the cloud be expensive?**

> Providers charge for data leaving their network, and often between regions, called egress. Incoming data is usually free, so chatty cross-region designs and large downloads can dominate a bill.

**7. What is a private endpoint?**

> A private address inside your virtual network for a managed service, such as a database or key vault. Traffic stays off the public internet, and the service's public access can be switched off entirely.

### What the cloud is

The **cloud** is computing rented on demand from a provider's data centres over the network. You ask for resources through a portal or an API, they appear in minutes, and you pay for what you use. The standard definition, from NIST, lists five characteristics: on-demand self-service, access over the network, resources pooled across many customers, rapid elasticity to grow and shrink, and measured, pay-per-use billing.

Three providers dominate. **AWS**, Amazon Web Services, launched its first services in 2006. Microsoft **Azure** followed in 2010 and is common in organizations built on Microsoft tools and .NET. Google Cloud is the third. Their services map closely onto each other under different names, and the ideas in this chapter apply to all of them. Examples here use Azure.

### From machines to finished software

Cloud services come at different levels, and each hands a different share of the work to the provider.

**IaaS**, infrastructure as a service, rents virtual machines, disks and networks. You install and patch the operating system and everything above it. It offers the most control and the most work.

**PaaS**, platform as a service, runs your code or data on a platform the provider manages: Azure App Service runs web apps, and managed databases such as Azure SQL Database run SQL Server without any server for you to maintain. Patching, backups and scaling are handled for you.

**SaaS**, software as a service, is finished software used over the internet, such as Microsoft 365 or a payroll system.

**Serverless** goes furthest: you provide functions or containers, and the platform runs them only when requests or events arrive, scales them automatically, including down to zero, and bills per execution. Azure Functions and AWS Lambda run functions; Azure Container Apps and Google Cloud Run run containers the same way. It suits spiky or event-driven work well, and brings cold starts, the delay when an idle function first runs.

### Who secures what

The **shared responsibility model** divides security between provider and customer. The provider always secures the physical facilities, the hardware and its own network, and the platform software of the services it manages. The customer always secures their data, their identities and access rules, their configuration and their code. In between, the line moves with the service model: with a virtual machine, patching the operating system is yours; with App Service, it is Microsoft's. Most cloud breaches come from the customer's side of the line: a storage account left public, an over-privileged identity, a database open to the internet.

### Regions and zones

Providers operate in dozens of **regions**: geographic areas with their own data centres, such as West Europe in the Netherlands or UK South in London. Choose regions close to users, for the latency reasons from Layers and packets (6.1), and where the law allows the data to be: passenger data may need to stay in particular countries, under data protection rules such as the GDPR.

Most regions contain several **availability zones**: physically separate data centres with independent power, cooling and networking, close enough for fast connections between them. A zone-redundant service keeps running when one data centre fails. Surviving the loss of a whole region needs a second region, with data replicated there, as in Non-relational stores (7.6), and a plan to switch over, which Reliability (12.5) discusses.

### The building blocks

Compute comes in the forms from Containers and Docker (11.3) and this chapter. From most control to least work, they are: virtual machines; managed Kubernetes, such as AKS or EKS; container platforms, such as Azure Container Apps; app platforms, such as App Service; and functions. Choose the least control that meets the need.

Data services are managed versions of the stores from Part 7: Azure SQL Database and managed PostgreSQL, Cosmos DB for documents, blob storage for files, from Storage and files (1.6), Service Bus for the queues from Background work and queues (8.7), and managed Redis for caches.

Networking connects them. A **virtual network** is your own private network inside the provider's, divided into subnets, with rules about what may talk to what. A **private endpoint** gives a managed service, such as a database or key vault, a private address inside your network, so its traffic never crosses the internet and its public access can be switched off. Load balancers, gateways and content delivery networks sit at the edge, handling TLS and routing, from HTTP (6.6).

### Identity and access

In the cloud, identity is the main security boundary: whoever can sign in with enough permissions controls everything, whatever the network says. Azure uses Microsoft Entra ID for identities, and role-based access control, from Authentication and authorization (10.2), for permissions. A **role assignment** grants a role, such as "Key Vault Secrets User", to an identity, at a scope: a whole subscription, a resource group, or one resource. Assign roles at the narrowest scope that works, which is least privilege from Thinking like an attacker (10.1).

Services should use managed identities, from Secrets and cryptography basics (10.5), to reach each other: the example's container app gets its own identity, and a role assignment lets that identity read secrets from one vault and nothing else. People should get access through groups, with MFA, and elevated rights only when needed and for a limited time.

Azure organizes resources in a hierarchy. A tenant holds the organization's identities. An **Azure subscription** is a billing and access boundary. A **resource group** is a container for resources that share a lifecycle, such as everything for the bookings API in staging. Tags on resources record owner, environment and cost centre.

### Infrastructure as code

Clicking resources together in a portal works once and can't be repeated exactly. **Infrastructure as code** (IaC) defines resources in files that live in the repository, are reviewed in pull requests and are applied by tools, usually from the pipeline in CI/CD pipelines (11.4). Staging and production are then created from the same files with different parameters, so they match. Changes can be previewed before they happen, and a lost environment can be recreated.

The common tools are declarative: you describe the resources you want, and the tool works out what to create, change or delete to get there, applying the same file twice changes nothing the second time. **Bicep** is Microsoft's language for Azure, compiled to Azure's JSON template format. **Terraform** is a widely used tool that works across many providers, with its own language. Pulumi lets you write infrastructure in general-purpose languages, including C# and TypeScript.

### Seeing it for real

This Bicep file defines the bookings API's resources: a log workspace, a Container Apps environment and the container app itself. The app has probes, scaling rules and a managed identity. There is also a key vault that uses role-based access, and one role assignment letting the app's identity read secrets from that vault. It compiles with the Bicep compiler without errors or warnings.

```bicep
// main.bicep: the bookings API's Azure resources. Preview with: az deployment group what-if -g rg-bookings-staging -f main.bicep
@description('Short environment name, used in resource names')
@allowed(['staging', 'production'])
param environmentName string

@description('The container image to run, by digest')
param image string

param location string = resourceGroup().location

var suffix = '${environmentName}-${uniqueString(resourceGroup().id)}'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-bookings-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-bookings-${suffix}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'ca-bookings-${environmentName}'
  location: location
  identity: { type: 'SystemAssigned' }                 // a managed identity: no stored credentials
  properties: {
    managedEnvironmentId: appEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'KeyVault__Uri', value: vault.properties.vaultUri }
          ]
          probes: [
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 } }
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 } }
          ]
        }
      ]
      scale: {
        minReplicas: environmentName == 'production' ? 2 : 0   // staging can scale to zero when idle
        maxReplicas: 10
      }
    }
  }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-bkg-${take(uniqueString(resourceGroup().id, environmentName), 13)}'
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true                        // access through role assignments, not access policies
    enablePurgeProtection: true
    softDeleteRetentionInDays: 90
  }
}

// Least privilege: the API's identity may read secrets from this one vault, and nothing else
var keyVaultSecretsUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource apiReadsSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, api.id, keyVaultSecretsUser)
  scope: vault
  properties: {
    roleDefinitionId: keyVaultSecretsUser
    principalId: api.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output url string = 'https://${api.properties.configuration.ingress.fqdn}'
```

A few details are worth noticing. Names are built from the environment and `uniqueString`, a stable hash of the resource group, because some names, such as key vaults, must be unique across all of Azure. Staging can scale to zero when idle, while production keeps two replicas. The image is a parameter, so the pipeline passes the digest it built, from CI/CD pipelines (11.4). And no secret appears anywhere: the app reaches the vault as itself.

The compiler checks resource types, property names and value types against Azure's API definitions. To see it work, misspell one property, as someone writing British English might:

```text
broken.bicep(77,5) : Warning BCP089: The property "enableRbacAuthorisation" is not allowed on objects of type "VaultProperties". Did you mean "enableRbacAuthorization"?
```

It is only a warning. Deployed as it stands, the vault would ignore the unknown property and silently use its older access-policy model instead of role-based access, a quiet security downgrade. So pipelines should fail on Bicep warnings, not only on errors, and run `az deployment group what-if`, which lists exactly what a deployment would create, change or delete before anything happens.

### Cost

Cloud billing works nothing like buying servers. You pay by the hour, second or request for what exists, so the main levers are different.

Turn off what isn't used: an idle development environment costs the same as a busy one unless it scales down, which is why the example lets staging scale to zero. **Autoscaling** adds capacity under load and removes it afterwards, the **elasticity** that on-premises servers can't offer. For steady workloads, reserved capacity or savings plans cut prices substantially in exchange for a one- or three-year commitment. Spot capacity, spare machines that can be taken back at short notice, is cheap for work that can be interrupted, such as batch jobs.

Watch data transfer. **Egress** charges apply to data leaving the provider's network, and often to traffic between regions and zones, while incoming data is usually free. Chatty designs that call across regions, or serve large downloads directly, can dominate a bill. Set budgets and alerts, tag everything with its owner and purpose, and review costs regularly. The practice of making engineering teams responsible for their cloud costs is called FinOps.

### In short

The cloud rents computing on demand, elastically, billed by use, mainly from AWS, Azure and Google Cloud. Services range from IaaS through PaaS to SaaS, with serverless running code only when events arrive. The provider secures the cloud; you secure your data, identities, configuration and code. Choose regions for latency and law, and spread across availability zones. Pick the least-control compute that works, use managed data services, and keep services on private endpoints in virtual networks. Identity is the boundary: role assignments at the narrowest scope and managed identities between services. Define everything as code with Bicep or Terraform, preview with what-if, and fail on warnings. Manage cost with scaling, commitments, egress awareness, budgets and tags.

### What goes wrong

**Public by accident.** A storage account, database or key vault is reachable from the internet with weak protection. Use private endpoints and turn off public access.

**Over-broad roles.** Owner on the whole subscription for a deployment identity or a developer. Assign narrow roles at narrow scopes.

**Portal-built environments.** Staging and production drift apart, and nobody can recreate either. Define infrastructure as code and deploy it from the pipeline.

**Ignoring warnings.** A misspelled property silently falls back to a weaker default. Treat Bicep and Terraform warnings as errors and review what-if output.

**One zone, one region.** A single data centre failure takes the service down. Use zone-redundant services, and plan for region failure where the business needs it.

**Surprise bills.** Idle environments, forgotten resources and cross-region traffic run up costs. Scale down, tag, set budgets and review.

**Assuming the provider secures everything.** Misconfiguration on the customer's side of the shared responsibility line causes most breaches. Know which side each control is on.

### Cost model

Compute is billed by time and size: a small container app replica or virtual machine costs tens of euros a month when always on, less when it scales to zero. Managed databases cost more than the machines they run on, and include patching, backups and high availability you would otherwise build yourself. Storage is cheap per gigabyte, while operations and egress add up at scale. Within a region, network calls between services take well under a millisecond; between regions, tens of milliseconds and a per-gigabyte charge. Reserved capacity typically cuts prices by 30 to 60% for committed, steady use.

### In other languages (optional)

**AWS.** EC2 for virtual machines, ECS, EKS and Lambda for containers and functions, RDS and DynamoDB for data, IAM roles and policies for access, and CloudFormation or the AWS CDK for infrastructure as code.

**Google Cloud.** Compute Engine, GKE, Cloud Run and Cloud Functions, Cloud SQL and Firestore, IAM, and Deployment Manager, though Terraform is more common there.

**Across clouds.** Terraform and Pulumi work with every provider, and Pulumi's C# and TypeScript support lets infrastructure live in the same language as the application.

### Key terms

- **Cloud**: Computing rented on demand from a provider's data centres over the network, billed by use.
- **AWS**: Amazon Web Services, the largest cloud provider, launched in 2006.
- **Azure**: Microsoft's cloud platform, common in organizations built on Microsoft tools.
- **IaaS**: Infrastructure as a service: renting virtual machines, disks and networks.
- **PaaS**: Platform as a service: running code or data on a platform the provider manages.
- **SaaS**: Software as a service: finished software used over the internet.
- **Serverless**: Running code that the platform starts on demand, scales automatically and bills per use.
- **Shared responsibility model**: The split of security duties between cloud provider and customer.
- **Region**: A geographic area where a provider has its own data centres.
- **Availability zone**: One of several physically separate data centres within a region.
- **Virtual network**: A private network within a cloud provider, divided into subnets with traffic rules.
- **Private endpoint**: A private address in your virtual network for a managed service, keeping its traffic off the internet.
- **Role assignment**: Granting a role to an identity at a scope, such as a subscription, resource group or resource.
- **Azure subscription**: An Azure billing and access boundary that holds resource groups.
- **Resource group**: A container for Azure resources that share a lifecycle.
- **Infrastructure as code**: Defining infrastructure in versioned files that tools apply repeatably. Often shortened to IaC.
- **Bicep**: Microsoft's declarative language for defining Azure resources.
- **Terraform**: A widely used infrastructure-as-code tool that works across many cloud providers.
- **Autoscaling**: Adding and removing capacity automatically as load changes.
- **Elasticity**: The ability to grow and shrink resources quickly with demand.
- **Egress**: Data leaving a provider's network, or crossing regions, which is usually charged.

### Where this leads

Seeing what services in the cloud are doing is in Observability (11.6). Splitting systems into services that run there is in Monoliths, modules and services (12.1), and scaling them in Scaling and performance (12.4). Designing for zone and region failures is in Reliability (12.5).

### Check yourself

1. Compile `main.bicep` with `az bicep build`, then misspell `targetPort` and read the diagnostic.
2. Add an Azure SQL database to `main.bicep` with public network access disabled and the container app's identity as its only way in.
3. Run `az deployment group what-if` for `main.bicep` against a test resource group and read the list of changes before deploying.
4. Estimate the monthly cost of the example in staging and production with the Azure pricing calculator, then again with staging scaled to zero overnight.
5. List the role assignments on a subscription you have access to, and find one that is broader than its holder needs.

### In practice: realistic examples

2 worked scenarios of the kind you meet at work: the situation, what goes wrong, the code that fixes it, and the lesson.

#### Example 1: Uploads larger than the gateway allows

Uploading a 40 MB scanned passport fails with 413 at the API gateway.

*Presigned upload directly to storage*

```csharp
app.MapPost("/documents/upload-url", (UploadRequest r, ClaimsPrincipal user, IAmazonS3 s3) =>
{
    if (r.Size > 50_000_000 || r.ContentType is not ("application/pdf" or "image/jpeg"))
        return Results.BadRequest();
    var key = $"uploads/{user.GetCustomerId()}/{Guid.NewGuid()}";        // server chooses the key
    var url = s3.GetPreSignedURL(new GetPreSignedUrlRequest
    {
        BucketName = "docs-prod", Key = key, Verb = HttpVerb.PUT,
        Expires = DateTime.UtcNow.AddMinutes(5), ContentType = r.ContentType,
    });
    return Results.Ok(new { url, key });
});
// confirm via an S3 event notification, then scan the file before using it
```

> **Lesson:** Send large files straight to object storage with short-lived presigned URLs; the API only authorizes.

#### Example 2: A storage account opened to the internet

A test bucket created in the console was public, and an index of customer files was found by a scanner.

*Infrastructure as code with secure defaults*

```bicep
resource docs 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'stdocsprod001'
  location: location
  kind: 'StorageV2'
  sku: { name: 'Standard_ZRS' }
  properties: {
    allowBlobPublicAccess: false
    publicNetworkAccess: 'Disabled'      // reached through a private endpoint only
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}
```

> **Lesson:** Portal-built resources drift and get opened by accident. Define infrastructure in code with private, secure defaults.

### Cloud: Azure vs AWS, Serverless, API Gateway, S3

<p class="src">From <em>.NET Backend Deep Dive</em> · Cloud: Azure vs AWS, Serverless, API Gateway, S3</p>

<a id="dn-34"></a>

#### Service equivalents

| Need | AWS | Azure |
|---|---|---|
| Virtual machines | EC2 | Virtual Machines |
| Serverless functions | **Lambda** | **Azure Functions** |
| Managed containers | ECS / Fargate / EKS | Container Apps / ACI / AKS |
| PaaS web hosting | Elastic Beanstalk / App Runner | App Service |
| Object storage | **S3** | **Blob Storage** |
| Managed SQL | RDS / Aurora | Azure SQL Database |
| NoSQL | DynamoDB | Cosmos DB |
| Queue | SQS | Queue Storage / Service Bus Queue |
| Pub-sub | SNS / EventBridge / Kinesis | Service Bus Topic / Event Grid / Event Hubs |
| API gateway | API Gateway | API Management (APIM) |
| Cache | ElastiCache | Azure Cache for Redis |
| Secrets | Secrets Manager / Parameter Store | Key Vault |
| Workload identity | IAM Role / IRSA | Managed Identity |
| Logging/monitoring | CloudWatch / X-Ray | Azure Monitor / App Insights |
| CDN | CloudFront | Azure Front Door / CDN |

**Philosophical difference:** AWS leans toward small composable primitives and very granular IAM. Azure leans toward built-in integration with the Microsoft ecosystem (Entra ID, .NET, Visual Studio) and a smoother developer experience for .NET teams.

<a id="dn-35"></a>

#### Serverless and Lambda

##### The idea
You supply code; the provider handles servers, scaling and patching. You pay per **invocation × duration × memory**. No traffic, no cost.

```csharp
// AWS Lambda with .NET (annotations framework)
public class Functions
{
    private static readonly IAmazonDynamoDB Db = new AmazonDynamoDBClient();
    // ^ static: reused across invocations in the same execution environment

    [LambdaFunction]
    [HttpApi(LambdaHttpMethod.Get, "/orders/{id}")]
    public async Task<Order> GetOrder(string id, ILambdaContext ctx)
    {
        ctx.Logger.LogInformation($"Fetching {id}");
        return await _repo.GetAsync(id);
    }
}
```

##### Cold start — the biggest issue, especially for .NET
With no warm environment available, AWS must create one, download the code, boot the .NET runtime, JIT, run your constructors, and only then handle the request. For .NET that can be **1–5 seconds**.

**How to reduce it:**

```xml
<PublishReadyToRun>true</PublishReadyToRun>   <!-- pre-JIT -->
<PublishAot>true</PublishAot>                 <!-- Native AOT: ~100ms cold start -->
<TieredCompilationQuickStartupJit>true</TieredCompilationQuickStartupJit>
```

- **Provisioned Concurrency** (AWS) / **Premium Plan – Always Ready** (Azure): keep instances warm — no cold starts, but you lose the "pay nothing when idle" benefit.
- More memory means proportionally more CPU, so startup is faster — sometimes a larger memory setting is actually **cheaper** because the function finishes sooner.
- Create clients (DynamoDB, HttpClient) in **static fields or the constructor**, not per invocation.
- Fewer packages, less reflection, a smaller DI container.

##### Limits worth memorizing (AWS Lambda)
- Maximum timeout **15 minutes**.
- Request/response payload **6MB** (synchronous), **256KB** (async events).
- `/tmp` **512MB** (configurable up to 10GB).
- Memory 128MB–10GB (CPU scales with memory).
- **Stateless** — no state between invocations, though static fields survive while the environment is warm.

##### When serverless is the wrong choice
- Jobs longer than 15 minutes → use ECS/Fargate/Batch.
- Steady, high 24/7 traffic → containers are usually **much cheaper**.
- Long-lived WebSockets, local state, or a need for consistently low latency.
- Traditional database connections: each concurrent Lambda holds a connection → **RDS connection exhaustion**. You need **RDS Proxy** (or DynamoDB / a serverless database).

<a id="dn-36"></a>

#### API Gateway — common issues

An API gateway sits in front of your backend handling routing, auth, rate limiting, transformation, caching, CORS and logging.

##### The problems you'll actually hit

**1. Integration timeout.** API Gateway has long capped backend integrations at **29 seconds** (AWS later allowed raising this for regional REST APIs). A slower backend means the client gets a 504 even though the backend is still working.
→ Switch to an **async pattern**: return `202 Accepted` with a job ID, then poll, webhook, or push over WebSocket.

**2. Payload limits.** 10MB for REST APIs. Large uploads through the gateway simply fail.
→ Use **presigned URLs** for S3 (see 9.5) instead of proxying bytes.

**3. Stacked cold starts.** Gateway → Lambda authorizer (cold) → Lambda handler (cold) doubles the latency.
→ Cache authorizer results (`authorizerResultTtlInSeconds`), or use the built-in JWT authorizer instead of a custom Lambda.

**4. Throttling and quotas.** Default account/stage limits exist; exceed them and you get `429 Too Many Requests`.
→ Configure usage plans and per-key throttles, and make clients retry with exponential backoff and jitter.

**5. Mapping templates are hard to debug.** VTL has no debugger and fails obscurely.
→ Prefer proxy integration (`AWS_PROXY`) and keep transformation logic in code.

**6. CORS.** Preflight `OPTIONS` needs separate configuration; forget it and the browser reports a CORS error while the backend looks perfectly healthy.

**7. Cost.** REST APIs are considerably more expensive than HTTP APIs. At high volume, consider HTTP API or an ALB.

**8. A single point of failure with limited visibility.** Gateway-level 5xx responses are hard to distinguish from backend errors unless you enable access logs plus X-Ray tracing and propagate trace IDs.

**9. Vendor lock-in.** Auth and transformation logic living in gateway configuration is painful to move to another cloud.

<a id="dn-37"></a>

#### S3 and security

##### Layers of defense

```
1. Block Public Access      <- enable at both account and bucket level
2. Bucket policy            <- deny by default, allow specific principals
3. IAM policy               <- least privilege per role
4. Encryption               <- SSE-S3 / SSE-KMS at rest + enforce HTTPS in transit
5. Versioning + MFA Delete  <- protection against accidental deletion and ransomware
6. Object Lock              <- WORM, immutable for a retention period
7. Access logging + CloudTrail data events  <- audit who read what
8. VPC Endpoint             <- traffic never leaves the private network
```

**The number one cause of S3 data leaks is a publicly readable bucket.** Turn on Block Public Access at the account level first.

```json
// Bucket policy: require HTTPS and require encryption on upload
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "DenyInsecureTransport",
      "Effect": "Deny",
      "Principal": "*",
      "Action": "s3:*",
      "Resource": ["arn:aws:s3:::my-bucket", "arn:aws:s3:::my-bucket/*"],
      "Condition": { "Bool": { "aws:SecureTransport": "false" } }
    },
    {
      "Sid": "DenyUnEncryptedUploads",
      "Effect": "Deny",
      "Principal": "*",
      "Action": "s3:PutObject",
      "Resource": "arn:aws:s3:::my-bucket/*",
      "Condition": {
        "StringNotEquals": { "s3:x-amz-server-side-encryption": "aws:kms" }
      }
    }
  ]
}
```

**Never** embed access keys in the application. Use IAM roles (EC2 instance profile, ECS task role, IRSA on EKS, Lambda execution role).

<a id="dn-38"></a>

#### Presigned URLs — upload and download

##### The problem they solve
If files flow through your API:

```
Client -> [API server buffers 500MB in RAM/disk] -> S3
```

That burns bandwidth and memory, hits the gateway payload limit, and makes your server the bottleneck.

With presigned URLs:

```
Client -> [API: request a URL]  (a few bytes)
API   -> signs a URL with its own credentials, returns it
Client -> [uploads straight to S3]  (bypassing your server)
```

Your server only does **authorization**; S3 does the data transfer.

##### How it works
The URL is signed with SigV4 using the server's credentials, embedding the signature and an expiry in the query string. S3 verifies the signature. Whoever holds the URL gets **exactly the permission that was signed** (bucket, key, method) for **exactly the window that was signed**.

##### Practical — download

```csharp
public string GetDownloadUrl(string key, string fileName)
{
    var req = new GetPreSignedUrlRequest
    {
        BucketName = "my-bucket",
        Key        = key,
        Verb       = HttpVerb.GET,
        Expires    = DateTime.UtcNow.AddMinutes(5),      // shorter is better
        // force a download with a friendly filename instead of inline rendering
        ResponseHeaderOverrides = new ResponseHeaderOverrides
        {
            ContentDisposition = $"attachment; filename=\"{fileName}\"",
            ContentType = "application/pdf"
        }
    };
    return _s3.GetPreSignedURL(req);
}
```

##### Practical — upload (simple PUT)

```csharp
public string GetUploadUrl(string key, string contentType)
{
    var req = new GetPreSignedUrlRequest
    {
        BucketName  = "my-bucket",
        Key         = key,
        Verb        = HttpVerb.PUT,
        Expires     = DateTime.UtcNow.AddMinutes(10),
        ContentType = contentType          // the client MUST send this exact Content-Type
    };
    return _s3.GetPreSignedURL(req);
}
```

```javascript
// Client
await fetch(uploadUrl, {
  method: 'PUT',
  headers: { 'Content-Type': 'image/png' },   // must match what was signed
  body: file
});
```

##### Presigned POST — better control for uploads
A presigned `PUT` **cannot limit file size** — someone can push 50GB. A `POST` policy can:

```json
{
  "expiration": "2026-08-23T12:00:00Z",
  "conditions": [
    {"bucket": "my-bucket"},
    ["starts-with", "$key", "uploads/user-123/"],
    {"acl": "private"},
    ["content-length-range", 0, 10485760],        // max 10MB  <-- the key control
    ["starts-with", "$Content-Type", "image/"]
  ]
}
```

##### Security checklist for presigned URLs

| Risk | Mitigation |
|---|---|
| The URL gets shared, or leaks into logs/referrers | Short TTL (1–15 min), never log the full URL |
| Anyone can request a URL | **Authorize before signing** — check the user may access that object |
| Users guess other people's keys | The server generates keys (`uploads/{userId}/{guid}`); never accept a client-supplied key |
| Enormous uploads | Presigned POST with `content-length-range` |
| Malicious files | Don't trust the client's `Content-Type` — scan for malware and check magic bytes after upload (trigger a Lambda from an S3 event) |
| Overwriting someone else's file | Per-user key prefixes plus a permission check on the prefix |
| Not knowing when the upload finished | Use **S3 event notifications** → SQS/Lambda → update the database, rather than trusting a client callback |
| Files over 5GB | Multipart upload (`CreateMultipartUpload` → presign each part → `CompleteMultipartUpload`) |
| Leaked server credentials | Use IAM roles, never hardcoded keys. A URL can't outlive the credentials that signed it |

##### Interview answer
> "A presigned URL lets a client talk to S3 directly without credentials: the server signs a URL with its IAM role and embeds the signature and expiry in the query string. That removes the bandwidth bottleneck at the API and sidesteps the gateway's payload limit. The important parts are authorizing before signing, keeping the TTL short, generating keys server-side per user, and using presigned POST so you can enforce `content-length-range`. Confirm completion via S3 event notifications rather than trusting the client."

---

### Cost Engineering

<p class="src">From <em>.NET Backend Deep Dive</em> · Cost Engineering</p>

<a id="dn-199"></a>

#### What actually drives the bill

Performance work and cost work are mostly the same work, and cost is the version that shows up on a spreadsheet someone reads.

For a typical .NET service, roughly in order:

1. **Over-provisioned compute.** Requests set to peak rather than actual usage, then multiplied by replicas across environments. Right-sizing from real utilization is usually the single largest saving available.
2. **Idle non-production environments.** Dev and staging running 24/7 for a team that works 40 hours a week is ~75% waste. Scale to zero on a schedule.
3. **Observability.** Logs and traces at 100% retention for 90 days routinely rival compute cost. Sample traces (Part 1.3), cut log levels in production, shorten retention, and watch **metric cardinality** (Part 28.2) — one unbounded label can multiply the bill.
4. **Data egress.** Cross-AZ and cross-region traffic and NAT gateway charges are invisible until the invoice. Keep chatty services in the same AZ; use VPC endpoints instead of routing through NAT.
5. **Storage that nobody deletes.** Old snapshots, unattached volumes, S3 without lifecycle policies, log buckets growing forever.
6. **Managed database tier.** Frequently sized for a load test from two years ago.

<a id="dn-200"></a>

#### Design choices with large cost consequences

```
Serverless vs containers:  Lambda is cheap when idle, expensive when steady.
                           Above roughly a few hundred sustained rps, containers win. (9.2)
CDN vs origin:             A cached response costs a fraction of one you compute. (41)
Cache hit ratio:           Below 80%, you're paying for Redis and the database. (6.4)
N+1 queries:               100 round trips instead of 1 is also 100x the DB capacity you provision. (2.2)
Chatty microservices:      Every hop is latency, retries, and cross-AZ bytes. (29.5)
Cosmos RU/s:               A cross-partition query can cost orders of magnitude more RUs. (43.2)
```

<a id="dn-201"></a>

#### Making it visible

Cost that nobody sees never improves. **Tag everything by team, service and environment**, put spend on a dashboard next to latency, and set budget alerts rather than discovering a spike at month end.

A genuinely useful metric for a business-facing service is **cost per request** or per order: it turns an abstract cloud bill into something that can be optimized, compared across services, and trended against traffic. A bill that grows linearly with traffic is healthy; one that grows faster is a bug.

**Where not to optimize:** engineer time is usually more expensive than infrastructure. Spending two weeks to save $80/month is a loss. Sort savings by annual value before touching anything.

---

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a201"></a>

#### Cloud primitives, mapped to .NET

| Need            | Azure                          | AWS                    | The .NET side                               |
|-----------------|--------------------------------|------------------------|---------------------------------------------|
| Run a container | Container Apps / AKS           | ECS / EKS / App Runner | Ordinary ASP.NET Core image                 |
| Run a function  | Azure Functions                | Lambda                 | Isolated worker model; AOT for cold start   |
| Relational data | Azure SQL, Flexible Server     | RDS, Aurora            | EF Core with the matching provider          |
| Document data   | Cosmos DB                      | DynamoDB, DocumentDB   | SDK, or EF Core Cosmos provider             |
| Cache           | Azure Cache for Redis          | ElastiCache            | `StackExchange.Redis`, HybridCache          |
| Queue / topic   | Service Bus                    | SQS / SNS              | MassTransit abstracts both                  |
| Event stream    | Event Hubs                     | Kinesis / MSK          | Kafka-compatible endpoints                  |
| Object storage  | Blob Storage                   | S3                     | Azure.Storage.Blobs / AWSSDK.S3             |
| Secrets         | Key Vault                      | Secrets Manager        | Configuration provider, or the SDK directly |
| Telemetry       | Application Insights / Monitor | CloudWatch / X-Ray     | OpenTelemetry exporter to either            |

Abstract the ones where portability is realistic (a queue, a blob store, a cache) behind your own narrow interface. Do not abstract the ones where you would lose the entire value — nobody builds a useful portable wrapper over Cosmos partition keys or DynamoDB's access patterns. Accept lock-in where it buys something and avoid it where it does not.

###### The same shape across three providers

| Need                   | Azure                        | AWS                         | GCP                 |
|------------------------|------------------------------|-----------------------------|---------------------|
| Run a container        | Container Apps               | ECS Fargate                 | Cloud Run           |
| Run a web app (PaaS)   | App Service                  | Elastic Beanstalk           | App Engine          |
| Kubernetes             | AKS                          | EKS                         | GKE                 |
| Functions              | Azure Functions              | Lambda                      | Cloud Functions     |
| Relational             | Azure SQL / Flexible Server  | RDS / Aurora                | Cloud SQL / AlloyDB |
| Object storage         | Blob Storage                 | S3                          | Cloud Storage       |
| Cache                  | Azure Cache for Redis        | ElastiCache                 | Memorystore         |
| Queue                  | Service Bus / Storage Queues | SQS                         | Pub/Sub             |
| Secrets                | Key Vault                    | Secrets Manager             | Secret Manager      |
| Identity for workloads | Managed Identity             | IAM Roles / IRSA            | Workload Identity   |
| Observability          | Application Insights         | CloudWatch / X-Ray          | Cloud Operations    |
| Config                 | App Configuration            | AppConfig / Parameter Store | Runtime Config      |

``` text
// the .NET pattern that keeps you portable: depend on the ABSTRACTION,
// register the provider at the composition root.
builder.Configuration.AddAzureAppConfiguration(...);   // or AddSystemsManager(...)
builder.Services.AddSingleton<IBlobStore, AzureBlobStore>();   // one line to swap

// Microsoft.Extensions.* abstractions that are genuinely provider-neutral:
//   IConfiguration        every provider has a source
//   ILogger               every provider has a sink
//   IDistributedCache     Redis anywhere
//   HealthChecks          consumed by every orchestrator
//   OpenTelemetry         exports to every backend
//
// and the ones that are NOT worth abstracting: queues and blob storage
// have genuinely different semantics per provider, and a lowest-common-
// denominator wrapper gives up the features you are paying for.
```

The honest position on multi-cloud: **portability is worth designing for and rarely worth paying much for**. Keep provider SDKs behind interfaces at the boundary so a migration is contained, use the `Microsoft.Extensions.*` abstractions where they exist, and otherwise use each provider's services properly. A codebase that avoids provider features to stay portable has paid a permanent cost for an option it will most likely never exercise.

### Compute: VMs to Serverless

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Compute: VMs to Serverless</p>

<p class="pd">Where code runs, from a physical machine to a function that exists for 40 milliseconds — and how to choose without following fashion.</p>

<a id="be-g666"></a>

#### Serverless and managed platforms

| Model    | You provide                | Platform handles                          | Examples                               |
|----------|----------------------------|-------------------------------------------|----------------------------------------|
| **IaaS** | OS and everything above    | Hardware, network                         | EC2, Azure VMs                         |
| **CaaS** | A container image          | Scheduling, scaling, restarts             | ECS, Container Apps, Cloud Run         |
| **PaaS** | Application code           | Runtime, scaling, patching                | App Service, Heroku, Elastic Beanstalk |
| **FaaS** | A function                 | Everything, including per-request scaling | Lambda, Azure Functions                |
| **Edge** | A small function or worker | Global distribution                       | Cloudflare Workers, Lambda@Edge        |

**Serverless suits**

- Spiky or unpredictable traffic
- Infrequent work — scale to zero means zero cost
- Event-driven processing (queue, storage, schedule)
- Small teams with no platform capacity
- Genuinely stateless, short operations

**Serverless struggles with**

- Steady high load — a VM is far cheaper per request
- Long-running work — hard execution time limits
- Cold-start-sensitive latency
- **Database connections** — N concurrent invocations open N connections
- Heavy local state or large dependencies

``` text
  THE SERVERLESS DATABASE PROBLEM

  1,000 concurrent function invocations, each opening its own connection
    -> 1,000 connections to a database that accepts 200
    -> connection errors, then a retry storm, then an outage

  each invocation is an isolated environment. Application-level pooling
  does not help, because each pool holds one connection.

  answers:
    a connection PROXY   RDS Proxy, PgBouncer, Data API - pools on the DB side
    an HTTP data API     Neon, PlanetScale, Supabase - no persistent connection
    reserved concurrency caps invocations, bounding the connection count
    do not use serverless for connection-heavy database work
```

| Cold start mitigation                       | Effect                                         |
|---------------------------------------------|------------------------------------------------|
| Provisioned concurrency / minimum instances | Removes cold starts; you pay for idle capacity |
| Smaller deployment package                  | Less to download and initialise                |
| Lazy-load dependencies                      | Only initialise what the request path needs    |
| Native AOT / lighter runtime                | Dramatically faster start for .NET and Java    |
| Keep-warm pings                             | A crude hack; unreliable under concurrency     |

> **\[Note\]**
>
> **The cost inversion worth knowing.** Serverless is dramatically cheaper at low and spiky volume and dramatically more expensive at sustained high volume. The crossover is usually somewhere around consistent single-digit requests per second. Model your actual traffic shape before choosing — a service running constantly at 50 requests per second is one of the clearest cases for a plain container on a reserved instance.

### Cloud: serverless, gateways and object storage

<p class="src">From <em>Backend Engineering</em> · Practical Answers · Cloud: serverless, gateways and object storage</p>

<p class="pd">The three cloud services a backend developer meets first — functions, API gateways and object storage — and the failure modes each one brings.</p>

<a id="be-q23"></a>

#### Serverless functions: what you gain and what you inherit

|                | AWS                               | Azure                                   |
|----------------|-----------------------------------|-----------------------------------------|
| Functions      | Lambda                            | Azure Functions                         |
| API front door | API Gateway / ALB                 | API Management / Functions HTTP trigger |
| Object storage | S3                                | Blob Storage                            |
| Queue          | SQS                               | Storage Queue / Service Bus             |
| Secrets        | Secrets Manager / Parameter Store | Key Vault                               |
| Identity       | IAM roles                         | Managed identity / Entra ID             |

``` csharp
// AWS Lambda, .NET, with the minimal-API style hosting
public class Function
{
    private static readonly HttpClient Http = new();     // static: survives
    private static readonly AmazonS3Client S3 = new();   // warm invocations

    public async Task<APIGatewayHttpApiV2ProxyResponse> Handler(
        APIGatewayHttpApiV2ProxyRequest req, ILambdaContext ctx)
    {
        ctx.Logger.LogInformation($"path={req.RawPath} requestId={ctx.AwsRequestId}");
        var body = await DoWorkAsync(req.Body, ctx);
        return new() { StatusCode = 200, Body = body,
                       Headers = new() { ["Content-Type"] = "application/json" } };
    }
}
```

``` text
the cold start, and why .NET needs care

request → no warm instance → download package → start runtime → JIT →
run your init → handle request        (hundreds of ms to seconds)

what actually helps, in order of effect:
  1. Native AOT or ReadyToRun publish     — removes most JIT work
  2. minimal dependencies                 — smaller package, faster load
  3. static clients (HttpClient, SDK)     — reused across warm invocations
  4. provisioned concurrency              — pre-warmed instances (costs money)
  5. do NOT open a DbContext per invocation against a small connection pool
     — serverless scales to hundreds of instances and exhausts the database.
       Use a proxy (RDS Proxy) or a serverless-friendly data API.
```

> **\[Trap\]**
>
> **Common trap.** Serverless removes server management, not distributed-systems work. You still own retries, idempotency (functions are invoked *at least* once), cold starts, per-invocation timeouts, concurrency limits, and a bill that scales with a runaway loop. The classic incident is a function writing to a queue that triggers itself.

<a id="be-q24"></a>

#### API Gateway: what it solves, and the issues it creates

A gateway is one public front door in front of many services: TLS termination, routing, auth, rate limiting, request/response transformation and usage plans. The problems it introduces are consistent across every vendor.

| Issue                    | What it looks like                                                      | What to do                                                                             |
|--------------------------|-------------------------------------------------------------------------|----------------------------------------------------------------------------------------|
| **Hard timeout**         | AWS API Gateway caps at 29–30 s; long requests fail at the gateway      | Make long work asynchronous: 202 + a job id the client polls, or WebSockets            |
| **Payload limit**        | 10 MB request cap; uploads fail with an opaque error                    | Never upload through the gateway — use presigned URLs straight to storage              |
| **Extra latency + cost** | A hop on every request, priced per million                              | Consider an ALB/Ingress for internal, high-volume traffic                              |
| **Auth split-brain**     | The gateway validates a JWT, the service validates it again differently | Validate once at the edge, pass verified identity inward; the service still authorises |
| **Config drift**         | Routes edited in the console, gone after redeploy                       | Infrastructure as code, always — the console is for reading                            |
| **Error mapping**        | Gateway 502/504s hide the real service error                            | Log the gateway request id, correlate it with the service trace id                     |

``` text
# the pattern that avoids both the timeout and the payload limits
POST /reports            → 202 Accepted { "jobId": "…", "statusUrl": "…" }
GET  /reports/{jobId}    → 200 { "status": "running" | "done", "url": "…" }

# and for files, never through the gateway:
POST /uploads/presign    → { "url": "https://bucket.s3…?X-Amz-Signature=…" }
PUT  <that url>          → the browser uploads DIRECTLY to S3
```

> **\[Why it matters\]** Most gateway incidents are not gateway bugs — they are architectural limits meeting a design that assumed an unbounded HTTP request. Knowing the three hard numbers (timeout, payload size, and burst quota) up front changes the design rather than producing an outage.

<a id="be-q25"></a>

#### S3 and object storage: security and presigned URLs

Object storage is the default home for files. It is also historically the most common source of public data leaks — not through exploits, but through buckets left readable.

``` text
the security baseline, in order

1. Block Public Access — on, at the ACCOUNT level, not just per bucket
2. Default encryption — SSE-S3 or SSE-KMS (KMS when you need key control
   and an audit trail of who decrypted what)
3. No IAM users, no long-lived access keys — roles and instance/pod identity
4. Least privilege — s3:GetObject on arn:…:bucket/tenant-42/*  NOT  bucket/*
5. Versioning + MFA delete — ransomware and accidental deletes
6. Server access logging / CloudTrail data events — who read what, when
7. Lifecycle rules — expire what you do not need; storage is cheap, breaches
   of five-year-old data are not
```

**Presigned URLs** are the mechanism that lets a browser talk to storage directly while your API stays in control. The URL carries a signature derived from your credentials, an expiry and the exact operation permitted — so the client never sees a credential and your server never proxies the bytes.

``` csharp
// DOWNLOAD: a short-lived link to one private object
public string GetDownloadUrl(string key, TimeSpan validFor)
{
    // authorise FIRST — the presigned URL itself has no idea who asked
    if (!_authz.CanRead(_currentUser, key)) throw new ForbiddenException();

    return _s3.GetPreSignedURL(new GetPreSignedUrlRequest
    {
        BucketName = _bucket,
        Key        = key,
        Verb       = HttpVerb.GET,
        Expires    = DateTime.UtcNow.Add(validFor),      // minutes, not days
        ResponseHeaderOverrides = new ResponseHeaderOverrides
        {
            ContentDisposition = $"attachment; filename=\"{SafeName(key)}\""
        }
    });
}

// UPLOAD: constrain what the client is allowed to send
public string GetUploadUrl(string contentType, long contentLength)
{
    if (contentLength > 25 * 1024 * 1024) throw new TooLargeException();
    if (!Allowed.Contains(contentType))   throw new UnsupportedMediaException();

    var key = $"tenant-{_tenant.Id}/uploads/{Guid.NewGuid():N}";  // server picks
    return _s3.GetPreSignedURL(new GetPreSignedUrlRequest        // the key!
    {
        BucketName  = _bucket,
        Key         = key,
        Verb        = HttpVerb.PUT,
        Expires     = DateTime.UtcNow.AddMinutes(10),
        ContentType = contentType,          // signed: client cannot change it
    });
}
```

``` text
the upload flow, and where each check happens

browser ──1── POST /uploads/presign  {name, type, size}
                  │  API: authenticate, authorise, validate type+size,
                  │       CHOOSE the key (never trust a client-supplied path)
              ◄─2── { url, key }
        ──3── PUT   (bytes go straight to S3 — never through your API)
        ──4── POST /uploads/complete {key}
                  │  API: HEAD the object to verify size/type actually match,
                  │       scan for malware if untrusted, THEN record it in the DB
              ◄─5── { fileId }

step 4 is the one teams skip — without it a client can presign, upload
something else, and never tell you, leaving orphaned objects and unverified
files that your database believes are valid.
```

> **\[Trap\]**
>
> **Common trap.** A presigned URL is a **bearer credential**: anyone holding it has that access until it expires. Never log it, never put it in a redirect that lands in a referrer header, keep expiry in minutes, and remember you cannot revoke one — the only mitigations are short lifetimes and, if you truly need revocation, rotating the signing key.

> **\[Trap\]**
>
> **Common trap.** Never let the client choose the object key. `key = userInput` allows `../`-style traversal into another tenant’s prefix and overwriting existing objects. The server generates the key, always, and prefixes it with the tenant.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a196"></a>

#### Infrastructure as code

Click-ops does not survive contact with a second environment. Infrastructure described in code is reviewable, diffable, repeatable and destroyable — and it is the only way a disaster recovery plan can be tested rather than hoped for.

| Tool                 | Language           | State                  | Notes                                                      |
|----------------------|--------------------|------------------------|------------------------------------------------------------|
| Bicep                | Purpose-built DSL  | Azure holds it         | Azure only; no state file to manage; excellent tooling     |
| Terraform / OpenTofu | HCL                | You manage state       | Multi-cloud, huge provider ecosystem, the industry default |
| Pulumi               | C#, TypeScript, Go | Managed or self-hosted | Real language: loops, types, unit tests                    |
| ARM / CloudFormation | JSON / YAML        | Provider holds it      | The layer underneath; rarely written by hand now           |
| Crossplane           | Kubernetes CRDs    | In-cluster             | Infrastructure as Kubernetes objects                       |

``` csharp
// Pulumi in C# - the same language as the application it provisions
var rg  = new ResourceGroup("orders-prod");
var plan = new Plan("plan", new() { ResourceGroupName = rg.Name, Sku = new() { Tier = "P1v3" } });

var sql = new Server("sql", new()
{
    ResourceGroupName = rg.Name,
    Administrators = new ServerExternalAdministratorArgs { AzureAdOnlyAuthentication = true }
});

var app = new WebApp("orders-api", new()
{
    ResourceGroupName = rg.Name,
    ServerFarmId = plan.Id,
    Identity = new ManagedServiceIdentityArgs { Type = ManagedServiceIdentityType.SystemAssigned }
});
```

1.  Everything in git, reviewed like application code.
2.  Plan on every pull request, so the diff is visible before merge.
3.  Apply from CI with a service principal — no human has standing write access to production.
4.  Remote, locked state so two engineers cannot apply at once.
5.  Separate stacks per environment; never a single template with a giant conditional.

> **\[Trap\]**
>
> **Common trap.** Drift is inevitable the first time someone fixes an incident in the portal. Run a scheduled plan that alerts on differences, and make reconciling drift a routine task rather than an archaeological dig six months later.

###### Worked example

What a reviewable infrastructure change looks like, and the properties that make it safe.

``` text
# terraform plan output, posted to the pull request by CI
Terraform will perform the following actions:

  # azurerm_linux_web_app.orders will be updated in-place
  ~ resource "azurerm_linux_web_app" "orders" {
      ~ site_config {
          ~ always_on            = false -> true
          ~ health_check_path    = null -> "/health/ready"
        }
    }

  # azurerm_postgresql_flexible_server.main will be UPDATED IN-PLACE
  ~ resource "azurerm_postgresql_flexible_server" "main" {
      ~ sku_name = "GP_Standard_D2s_v3" -> "GP_Standard_D4s_v3"
        # NOTE: this causes a restart with ~60s downtime

  # azurerm_storage_container.exports will be DESTROYED
  - resource "azurerm_storage_container" "exports" { ... }

Plan: 0 to add, 2 to change, 1 to destroy.
```

The destroy line is the entire point of a plan-on-PR workflow. Someone renamed a resource in the code, Terraform interpreted it as delete-and-recreate, and a reviewer can see that before it happens rather than afterwards. Without the plan in the PR, that is a data-loss incident discovered by a support ticket.

1.  **Plan runs on every pull request** and posts the output as a comment. No exceptions.
2.  **Any line containing `destroy` or `forces replacement` requires a second approver.** Automate the check rather than relying on people reading carefully.
3.  **Apply runs only from the default branch**, using a workload identity, with remote locked state.
4.  **Protect stateful resources** with `prevent_destroy` lifecycle rules, so a mistake fails the plan instead of the database.
5.  **Run a nightly plan against production** with no apply. A non-empty diff is drift — someone changed something in the portal, and you want to know within a day rather than at the next release.

``` text
resource "azurerm_postgresql_flexible_server" "main" {
  # ...
  lifecycle {
    prevent_destroy = true                       # a plan that would delete this FAILS
    ignore_changes  = [zone, high_availability]  # fields the platform manages
  }
}
```

###### Structuring modules so environments cannot drift

``` text
infra/
  modules/
    app-service/       main.tf  variables.tf  outputs.tf     # one reusable component
    postgres/
    observability/
  envs/
    dev/               main.tf  terraform.tfvars  backend.tf # composition per environment
    staging/
    prod/
```

``` text
# envs/prod/main.tf - the SAME module, different inputs
module "orders_api" {
  source = "../../modules/app-service"

  name            = "orders-api"
  environment     = "prod"
  sku             = "P2v3"
  instance_count  = 6
  health_check    = "/health/ready"
  private_endpoint = true          # dev sets false; the DIFFERENCE is visible in one place
}
```

The property this buys: the shape of every environment is identical because it is literally the same code, and every intentional difference appears as an explicit variable in a small file you can read in one sitting. Copy-pasted per-environment templates always diverge, and the divergence is invisible until staging stops predicting production.

| Anti-pattern                        | Consequence                                                     |
|-------------------------------------|-----------------------------------------------------------------|
| One state file for all environments | A dev apply can destroy production; blast radius is everything  |
| Copy-pasted environment folders     | Silent drift; staging no longer tests what prod runs            |
| Secrets in `tfvars`                 | They land in state, which is stored in plain text               |
| No remote state locking             | Two concurrent applies corrupt the state file                   |
| Manual portal changes               | The next apply reverts them, usually during an unrelated deploy |

<a id="be-a197"></a>

#### Environments, naming and structure

The boring conventions matter more than the tooling, because they are what make an unfamiliar system navigable at 3am.

``` yaml
{org}-{app}-{component}-{env}-{region}   ->  acme-orders-api-prod-weu
                                             acme-orders-sql-prod-weu
                                             acme-orders-kv-prod-weu

tags: owner=payments-team  cost-centre=CC1042  env=prod
      data-classification=confidential  managed-by=terraform
```

| Environment  | Purpose                     | Data                            | Who deploys                 |
|--------------|-----------------------------|---------------------------------|-----------------------------|
| Local        | Development                 | Synthetic, in containers        | The developer               |
| CI ephemeral | Automated tests             | Seeded per run, destroyed after | The pipeline                |
| Staging      | Pre-production verification | Anonymised production-like      | The pipeline, automatically |
| Production   | Real users                  | Real                            | The pipeline, with approval |

- Staging must match production in *shape* — same topology, same TLS, same auth provider — even if it is smaller. A staging environment that differs structurally tests nothing.
- Never copy production data into a lower environment without anonymising it. This is a GDPR obligation, not a preference.
- Tag everything with an owner and a cost centre on day one; retrofitting tags across 400 resources is a genuinely awful week.
- Give each environment its own identity, its own key vault and its own network. Shared secrets across environments make a staging compromise a production compromise.

###### A naming scheme that survives growth

``` text
# the shape that stops working at about twenty resources
myapp-db
myapp-db-test
myapp-db2
myapp-db-new
prod-database-DO-NOT-DELETE

# a scheme with a grammar: <org>-<system>-<component>-<env>-<region>[-<n>]
acme-orders-sql-prd-weu-01
acme-orders-app-prd-weu-01
acme-orders-kv-prd-weu-01
acme-orders-sql-tst-weu-01

# environment codes, fixed and short: dev tst acc prd
# region codes, fixed: weu neu eus
# and it SORTS usefully, which matters once a list is long.
```

| Tag every resource with | Answers                                                         |
|-------------------------|-----------------------------------------------------------------|
| `owner`                 | Who do I ask before deleting this?                              |
| `system`                | Which application does it belong to?                            |
| `environment`           | Can I break it?                                                 |
| `cost-centre`           | Who pays?                                                       |
| **`managed-by`**        | **Which repository creates it?** — the most useful tag there is |
| `data-classification`   | What compliance rules apply?                                    |

`managed-by` earns its place because the most common question about an unfamiliar resource is “where does this come from, and will changing it be reverted?” A tag pointing at the repository and module answers it in seconds and makes untagged resources visibly suspicious — anything without it was created by hand and is drift by definition.

> **\[Note\]**
>
> **Decide it once, enforce it in policy.** Naming and tagging conventions written in a wiki are aspirations. Enforced with Azure Policy, AWS SCPs or a Terraform validation rule, they are facts. The cost of retrofitting a convention across 400 existing resources is high enough that it usually never happens — so this is worth an hour before the first resource exists.

<a id="be-a198"></a>

#### Network infrastructure and private connectivity

``` text
  VNet 10.0.0.0/16
    subnet 10.0.1.0/24   ingress / application gateway   <- the only public entry
    subnet 10.0.2.0/24   application (AKS nodes, App Service integration)
    subnet 10.0.3.0/24   private endpoints: SQL, Key Vault, Storage, Service Bus
    subnet 10.0.4.0/24   management / bastion

  no public IP on the database. no public IP on the vault. one front door.
```

| Concept              | What it does                                       | Why you care                                                |
|----------------------|----------------------------------------------------|-------------------------------------------------------------|
| Private endpoint     | Gives a PaaS service a private IP inside your VNet | The database stops being reachable from the internet at all |
| Service endpoint     | Restricts a PaaS firewall to your subnet           | Cheaper, weaker — still traverses the public backbone       |
| NSG / security group | Allow and deny rules per subnet or NIC             | The last line of defence when something is misconfigured    |
| Egress control       | Restricts outbound traffic to an allow-list        | Data exfiltration and command-and-control prevention        |
| Private DNS zone     | Resolves the PaaS name to the private IP           | Forget this and you silently keep using the public path     |

This is also where the trickiest .NET connectivity bugs come from: an app that resolves `mydb.database.windows.net` to a public IP because the private DNS zone is not linked to the VNet will connect successfully and be firewalled off — producing a timeout rather than a clear error. Always verify what the name resolves to from inside the pod, not from your laptop.

###### What changes in your .NET configuration

``` text
  the shape you are aiming for

    internet
       |
    [ WAF / load balancer ]        the ONLY thing in a public subnet
       |
    app tier   (private subnet)    no inbound from the internet at all
       |
    data tier  (private subnet)    reachable ONLY from the app tier
                                   no route to an internet gateway

  and the piece that matters for .NET specifically: PRIVATE ENDPOINTS.

    without: your app resolves mydb.database.windows.net to a PUBLIC ip
             and traverses the internet (encrypted, but out and back)

    with:    the same hostname resolves to a PRIVATE ip inside your network
             -> the connection string does not change
             -> DNS does the work
             -> traffic never leaves the provider backbone
             -> and you can then disable public access entirely
```

| Symptom after enabling private endpoints    | Cause                                                                                |
|---------------------------------------------|--------------------------------------------------------------------------------------|
| Works from a VM, fails from the app service | The app is not VNet-integrated — it has no route to the private IP                   |
| Resolves to a public IP still               | The private DNS zone is not linked to that virtual network                           |
| Works locally, fails when deployed          | Your machine resolves publicly; the deployed app resolves privately                  |
| Intermittent timeouts                       | A stateful firewall dropping idle pooled connections — bound the connection lifetime |
| Migrations fail from the pipeline           | The build agent is outside the network. Use a private agent or a jump path           |

> **\[Note\]**
>
> **The connection string should not change.** That is the test of a well-built private network: the application's configuration is identical in every environment, and the network decides where the name resolves. If you find yourself maintaining different connection strings per environment to route traffic, the routing is being done in the wrong layer.

### Infrastructure as Code

<p class="src">From <em>Backend Engineering</em> · Backend Platform · Infrastructure as Code</p>

<p class="pd">Defining infrastructure in files rather than clicking. State, drift, modules and the review discipline that makes it safe — plus the failure modes that only appear once several people share a codebase.</p>

<a id="be-g688"></a>

#### Declarative infrastructure and state

The core idea is small: describe the desired end state, and let a tool compute the difference from reality. The complexity is entirely in how the tool knows what reality currently is.

``` text
  the three-way comparison every plan performs

    CONFIG          what you wrote in .tf files          (desired)
    STATE           what Terraform believes exists       (recorded)
    REAL WORLD      what the cloud API reports           (actual)

  refresh:  state <- real world
  plan:     diff(config, state)  ->  the actions to take
  apply:    execute, then update state

  which is why STATE IS CRITICAL and why losing it is painful:
  without state, Terraform does not know it created anything, so a plan
  proposes creating everything again - alongside the resources that
  already exist.
```

| State concern           | Handling                                                                                 |
|-------------------------|------------------------------------------------------------------------------------------|
| **Never local**         | A remote backend: S3+DynamoDB, Azure Storage, GCS, Terraform Cloud                       |
| **Locking**             | Two concurrent applies corrupt state. The backend must support locking                   |
| **It contains secrets** | Passwords and keys are stored in plaintext in state. Encrypt it, restrict access tightly |
| **Versioning**          | Enable bucket versioning, so a bad apply can be recovered                                |
| **Splitting**           | One giant state is slow and high-blast-radius. Split by lifecycle and ownership          |
| **Import**              | Bringing existing resources under management — `import` blocks make this declarative     |

``` bash
terraform {
  required_version = "~> 1.9"
  backend "s3" {
    bucket         = "acme-tfstate"
    key            = "prod/network/terraform.tfstate"
    region         = "eu-west-1"
    dynamodb_table = "tf-locks"        # locking
    encrypt        = true
  }
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 5.60" }   # PIN providers
  }
}

# split state by rate of change and by blast radius:
#   prod/network/       changes rarely, breaks everything      -> its own state
#   prod/data/          databases, storage                     -> its own state
#   prod/platform/      cluster, ingress                       -> its own state
#   prod/apps/orders/   changes daily, low blast radius        -> its own state
#
# read across states with a data source, not by merging them:
data "terraform_remote_state" "network" {
  backend = "s3"
  config  = { bucket = "acme-tfstate", key = "prod/network/terraform.tfstate", region = "eu-west-1" }
}
```

<a id="be-g689"></a>

#### Modules, environments and avoiding copy-paste

``` csharp
# a module is a reusable unit with a clear interface.
# the discipline: modules take inputs and produce outputs. They do NOT
# read remote state or reach outside themselves - that makes them
# untestable and couples them to one deployment.

module "api" {
  source = "../../modules/service"

  name         = "orders-api"
  environment  = var.environment
  image        = var.image_digest
  cpu          = var.environment == "prod" ? 2048 : 512
  min_replicas = var.environment == "prod" ? 3 : 1
  subnet_ids   = data.terraform_remote_state.network.outputs.private_subnet_ids
}
```

| Environment strategy          | How                                                     | Trade                                                                 |
|-------------------------------|---------------------------------------------------------|-----------------------------------------------------------------------|
| **Directory per environment** | `envs/prod/`, `envs/test/`, each calling shared modules | Explicit and readable. Some duplication. **Usually the right choice** |
| **Workspaces**                | One codebase, `terraform workspace select prod`         | Easy to apply to the wrong one. Conditionals proliferate              |
| **Terragrunt**                | A wrapper generating the boilerplate                    | Less duplication; another tool to learn                               |
| **Separate repositories**     | Hard isolation per environment                          | Strongest separation; changes must be replicated                      |

- **Pin provider and module versions.** An unpinned provider upgrade changes a plan you did not intend to change.
- **Keep modules small and composable** — a module that provisions “an entire environment” cannot be reused or reasoned about.
- **Do not parameterise everything.** A module with forty variables is a worse abstraction than two modules.
- **Avoid `count` for lists that change** — removing the second of three items re-creates the third. Use `for_each` with stable keys.
- **Output what other states need**, and treat outputs as a public interface you cannot casually rename.

> **\[Trap\]**
>
> **Common trap.** `count` indexes resources positionally, so removing an element shifts every subsequent one — and Terraform interprets a shift as *destroy and recreate*. On a list of database instances or DNS records that is a genuine outage produced by an apparently harmless edit. `for_each` keys resources by a stable string instead, so removals affect only the removed item.

<a id="be-g690"></a>

#### Drift, review and safe applies

``` text
  DRIFT: reality no longer matches the code

  causes
    someone changed it in the console during an incident
    an autoscaler or controller modified a resource
    a provider default changed between versions
    a resource was deleted manually

  the danger: the next apply "corrects" the drift - which may mean
  reverting an emergency fix that is holding production together,
  with no warning beyond one line in a plan nobody read carefully.

  controls
    run `terraform plan` on a SCHEDULE and alert on any diff
    make console write access exceptional and audited
    use `ignore_changes` for fields legitimately managed elsewhere
      (e.g. replica counts owned by an autoscaler)
```

``` text
# the plan must be the artifact that gets applied. Do not plan and then
# apply separately - the world may have changed in between.
terraform plan -out=tfplan
terraform show -json tfplan | jq '.resource_changes[] | select(.change.actions[] | contains("delete"))'
terraform apply tfplan          # applies EXACTLY what was reviewed

# gate destructive changes in CI
if terraform show -json tfplan | jq -e '.resource_changes[]
     | select(.change.actions | index("delete"))' > /dev/null; then
  echo "::warning::plan contains deletions - requires senior approval"
fi

# and protect the things that must never be destroyed
resource "aws_db_instance" "main" {
  lifecycle {
    prevent_destroy = true                    # apply FAILS rather than deleting
    ignore_changes  = [ engine_version ]      # patched by the maintenance window
  }
}
```

| Practice                                | Prevents                                                  |
|-----------------------------------------|-----------------------------------------------------------|
| Plan posted to the pull request         | Changes merged without anyone seeing the effect           |
| `prevent_destroy` on stateful resources | A refactor silently deleting a database                   |
| Separate plan and apply credentials     | A read-only plan on untrusted PRs                         |
| Policy as code (OPA, Sentinel, Checkov) | Public buckets, unencrypted volumes, open security groups |
| Drift detection on a schedule           | Discovering drift during an unrelated change              |
| Small, frequent applies                 | A six-month plan nobody dares run                         |

> **\[Note\]**
>
> **The plan is the review.** A pull request changing infrastructure code is not reviewable on its own — a two-line variable change can produce a plan that replaces a database. Post the plan output into the pull request automatically, and make the reviewer's job reading the plan rather than reading the diff. Every serious infrastructure incident I would expect you to encounter starts with a plan that nobody read to the end.

###### State locking, imports and the operations that go wrong

``` text
# state must be REMOTE and LOCKED, or two engineers corrupt it
terraform {
  backend "s3" {
    bucket         = "acme-tfstate"
    key            = "prod/network/terraform.tfstate"
    region         = "eu-west-1"
    dynamodb_table = "terraform-locks"    # the LOCK. Without it, two
                                          # concurrent applies interleave
                                          # writes and the state is garbage.
    encrypt        = true
  }
}
# and one state file PER environment and per blast radius. A single state
# holding everything means every apply risks everything, and every plan
# takes minutes.
```

| Operation                  | Command              | Danger                                                                        |
|----------------------------|----------------------|-------------------------------------------------------------------------------|
| Adopt an existing resource | `terraform import`   | Config must match reality *exactly*, or the next apply destroys and recreates |
| Stop managing a resource   | `state rm`           | It becomes orphaned — still billing, now unmanaged                            |
| Rename in code             | `moved` block        | Without it, Terraform destroys and recreates                                  |
| Break a stuck lock         | `force-unlock`       | **Only** if you are certain no apply is running                               |
| Recreate one resource      | `-replace=`          | Downtime for that resource                                                    |
| Inspect drift              | `plan -refresh-only` | Read-only; safe and underused                                                 |

``` text
  READING A PLAN - the four symbols, and which one to fear

    +  create        new. Usually fine.
    ~  update in place   fine.
    -/+ REPLACE      DESTROY then create.
                     THIS IS THE ONE. For a database, a volume, or a
                     static IP, this is an outage or data loss - and it
                     is easy to miss in a 400-line plan.
    -  destroy       intended, or a catastrophe. Read carefully.

  the discipline that prevents incidents:

    plan output is posted to the pull request, AND
    the reviewer's job is specifically to look for -/+ , AND
    apply uses the SAVED plan file, not a fresh one:

      terraform plan -out=tfplan
      terraform apply tfplan        # applies EXACTLY what was reviewed

    running a fresh plan at apply time means the reviewed plan and the
    applied plan can differ - which defeats the review entirely.
```

> **\[Trap\]**
>
> **Common trap.** `count` uses a *positional* index, so removing the second of five items shifts every subsequent resource's address — Terraform then destroys and recreates four resources that did not change. Use `for_each` with stable string keys instead; the address becomes `resource["name"]` and removals affect only that one.

<a id="be-g691"></a>

#### Beyond Terraform: the wider IaC picture

| Tool                     | Model                                           | Fits                                                         |
|--------------------------|-------------------------------------------------|--------------------------------------------------------------|
| **Terraform / OpenTofu** | Declarative HCL, provider ecosystem             | The default for cloud provisioning                           |
| **Pulumi**               | Real languages (TypeScript, C#, Go)             | Teams who want loops, types and tests in a familiar language |
| **Bicep / ARM**          | Azure-native declarative                        | Azure-only estates; no state file to manage                  |
| **CloudFormation / CDK** | AWS-native; CDK generates it from code          | AWS-only; CDK is pleasant, the generated templates are not   |
| **Crossplane**           | Provision cloud resources *via* Kubernetes CRDs | Teams already committed to Kubernetes as the control plane   |
| **Ansible**              | Imperative-ish configuration management         | Configuring machines, not provisioning them                  |
| **Packer**               | Builds machine images                           | Immutable infrastructure — pairs with Terraform              |

``` text
  the layers, and the tool for each

  1  IMAGE BUILD        Packer, Dockerfile          what is ON the machine
  2  PROVISIONING       Terraform, Bicep, Pulumi    what machines EXIST
  3  CONFIGURATION      Ansible, cloud-init         how they are SET UP
  4  DEPLOYMENT         Helm, Argo CD, pipelines    what RUNS on them
  5  POLICY             OPA, Kyverno, Sentinel      what is ALLOWED

  a common mistake is using one tool for all five. Terraform deploying
  application code, or Ansible provisioning cloud resources, both work
  and both fight the tool's model - which surfaces as slow runs,
  unreliable idempotency, and state that does not reflect reality.
```

- **Everything in version control**, including the pipeline definitions and the policies.
- **Nobody changes production through a console**, except during a declared incident — and then it goes back into code the same week.
- **Tag every resource** with owner, environment, cost centre and the repository that manages it. The last one answers “where does this come from?” in seconds.
- **Test what you can** — `terraform validate`, `tflint`, policy checks in CI, and Terratest for modules that matter.
- **Bootstrap deliberately.** The state bucket and the CI role cannot themselves be created by the pipeline that needs them; document how they were made.

### Infrastructure & Platform

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Infrastructure & Platform</p>

<p class="pd">The layer beneath your containers: how environments are described in code, how identity and networking are wired, what the cloud primitives map to in .NET terms, and what it costs to run.</p>

<a id="be-a202"></a>

#### Service mesh and Dapr

**Service mesh (Istio, Linkerd)**

- A sidecar proxy intercepts all network traffic
- mTLS, retries, timeouts, traffic splitting, tracing — with zero application code
- Language-agnostic; ideal for polyglot estates
- Real operational complexity and per-pod overhead

**Dapr**

- A sidecar exposing building blocks over HTTP/gRPC
- State store, pub/sub, secrets, bindings, actors, workflows
- You call an API instead of a vendor SDK — swap Redis for Cosmos in config
- Another abstraction to learn, and it leaks

``` text
# a Dapr component: change this file, not your code, to swap the backing store
apiVersion: dapr.io/v1alpha1
kind: Component
metadata: { name: statestore }
spec:
  type: state.redis
  version: v1
  metadata:
    - name: redisHost
      value: cache:6379
```

The honest assessment for most .NET teams: the resilience, mTLS and observability features that motivate a mesh are largely available in-process through `Microsoft.Extensions.Http.Resilience`, OpenTelemetry and Aspire's service defaults, at a fraction of the operational cost. Adopt a mesh when you have a genuinely polyglot estate or a compliance requirement for universal mTLS — not because the architecture diagram looks better.

###### Two different things, frequently confused

``` text
  SERVICE MESH (Istio, Linkerd)      DAPR

  an INFRASTRUCTURE concern.          an APPLICATION concern.
  your code is UNCHANGED.             your code CALLS Dapr's APIs.

  sidecar intercepts network          sidecar provides building blocks
  traffic transparently:                over HTTP/gRPC:
    mTLS between services               state store
    retries, timeouts                   pub/sub
    traffic splitting, canaries         service invocation
    observability                       secrets
                                        bindings, actors

  -> you get security and traffic     -> you get portable abstractions
     control for free                    over swappable backends
  -> nothing in the app knows         -> the app is coupled to Dapr's API
```

``` csharp
// Dapr from .NET - note that the app now depends on Dapr's model
var client = new DaprClientBuilder().Build();

await client.SaveStateAsync("statestore", key, order);      // Redis? Cosmos? config decides
await client.PublishEventAsync("pubsub", "orders", orderPlaced);
var res = await client.InvokeMethodAsync<Req, Res>("billing", "charge", req);

// the appeal: swapping Redis for Cosmos is a YAML change, not a code change.
// the cost: an extra process per pod, an extra network hop per call, and
// an abstraction that is lowest-common-denominator across every backend.
```

| Consider a mesh when                           | Consider Dapr when                                |
|------------------------------------------------|---------------------------------------------------|
| You need mTLS everywhere without touching code | You want portable state and pub/sub abstractions  |
| Canary and traffic-splitting are requirements  | Polyglot services need one consistent model       |
| Many services, one platform team               | The actor model or workflows fit your domain      |
| Zero-trust networking is mandated              | You are on Container Apps, where Dapr is built in |

> **\[Note\]**
>
> **Both are answers to problems you may not have.** For a handful of .NET services, `IHttpClientFactory` with the standard resilience handler covers retries, timeouts and circuit breaking; OpenTelemetry covers observability; and TLS at the ingress covers encryption in transit. A mesh earns its operational cost at dozens of services or under a zero-trust mandate. Dapr earns its extra hop when portability across backends is a genuine requirement rather than an aspiration.

<a id="be-a203"></a>

#### Cost, and why engineers should care

Cloud cost is an engineering output, not a finance problem. The same feature can cost ten times more depending on decisions made in an afternoon.

| Cost driver                  | Typical waste                                        | Fix                                                              |
|------------------------------|------------------------------------------------------|------------------------------------------------------------------|
| Over-provisioned compute     | Requests set to what the pod peaks at, running 24/7  | Right-size from real metrics; autoscale; scale to zero off-hours |
| Cross-AZ / egress traffic    | Chatty services scattered across zones               | Zone-aware routing; batch calls; keep data local                 |
| Cosmos / DynamoDB throughput | Provisioned for peak, cross-partition queries        | Serverless or autoscale tiers; fix the partition key             |
| Log ingestion                | Debug logging in production at full volume           | Sample; drop health-check noise; shorten retention               |
| Idle non-production          | Staging identical to production, running all weekend | Schedule shutdowns; ephemeral PR environments                    |
| Untagged orphans             | Resources nobody can identify or claim               | Mandatory tags enforced by policy                                |

Two habits go a long way: put the monthly cost of your service on the same dashboard as its latency, and make cost part of design review. “This will add roughly €900 a month” changes the conversation from an architecture debate into a business decision, which is where it belongs.

###### Where the money actually goes

``` text
  a typical cloud bill for a .NET service, roughly ranked

  1  COMPUTE          usually the largest, and usually over-provisioned.
                      instances sized for a peak that happens twice a year.
  2  DATA TRANSFER    the one that surprises people:
                        cross-AZ traffic between YOUR OWN services
                        NAT gateway processing on every container pull
                        egress to the internet
  3  MANAGED DATABASE  instance + storage + IOPS + backup, priced separately
  4  STORAGE           and the forgotten part: snapshots that never expire
  5  OBSERVABILITY     frequently EXCEEDS the compute it observes.
                      usually one high-cardinality metric label, or debug
                      logging left on
  6  IDLE RESOURCES    non-production running at nights and weekends,
                      orphaned disks, unattached IPs, old snapshots
```

| Engineering decision                     | Cost consequence                                           |
|------------------------------------------|------------------------------------------------------------|
| Right-sizing from measured usage         | Typically the single largest saving available              |
| Reserved or savings-plan commitment      | 30–60% for steady workloads. Pure arithmetic               |
| Scaling non-production to zero overnight | Roughly 65% off those environments                         |
| Native AOT or trimming                   | Smaller instances, faster scale-out, less memory           |
| Fixing an N+1                            | Fewer database IOPS, a smaller instance tier               |
| Caching a hot read                       | Fewer database resources; often a tier reduction           |
| A high-cardinality metric label          | Can multiply the observability bill by orders of magnitude |
| Debug logging in production              | Frequently the largest single line on the log bill         |
| VPC endpoints instead of NAT             | Removes per-GB charges on image pulls and storage access   |

> **\[Note\]**
>
> **Attribution is what makes it actionable.** Tag everything with a system and a cost centre, put a per-service cost figure on the same dashboard as its latency, and set a budget alert. A team that can see its own spend usually optimises it without being asked; a single unattributed bill produces a quarterly argument and no change. This is one of the few areas where the reporting is genuinely most of the fix.

### Deployment & DevOps

<p class="src">From <em>Backend Engineering</em> · C# & .NET · Deployment & DevOps</p>

<p class="pd">From dotnet publish to a running production system: containers, orchestration, pipelines, release strategies, and the operational habits that keep it up.</p>

<a id="be-a221"></a>

#### Hosting options and where legacy fits

| Platform                              | You manage    | Suits                                                       |
|---------------------------------------|---------------|-------------------------------------------------------------|
| Azure App Service / Elastic Beanstalk | The app only  | Straightforward web apps; fastest path off a VM             |
| Azure Container Apps / AWS App Runner | The container | Microservices without running Kubernetes                    |
| AKS / EKS / GKE                       | The cluster   | Many services, custom networking, real platform needs       |
| Azure Functions / Lambda              | A function    | Event-driven and spiky work; watch cold starts (or use AOT) |
| VM + IIS                              | Everything    | .NET Framework, Windows dependencies, on-prem               |

##### Keeping .NET Framework running well

- One app pool per application, with recycling configured deliberately — not the default nightly recycle that drops sessions.
- Set `<compilation debug="false">` in production; `debug="true"` disables timeouts and batch compilation.
- Application Initialization module so the first user after a recycle does not pay the JIT cost.
- `ServicePointManager.DefaultConnectionLimit` defaults to 2 per host on Framework — raise it or your outbound calls serialise.
- Web Deploy or an MSDeploy package from the same CI pipeline as everything else; do not deploy by copying folders.

###### The decision, and the realistic path for a Framework estate

| Option                                       | Fits                                           | Watch for                                                             |
|----------------------------------------------|------------------------------------------------|-----------------------------------------------------------------------|
| **Azure App Service** / Elastic Beanstalk    | Standard web apps; least operational work      | Limited control; scale-out cost                                       |
| **Container Apps** / Cloud Run / ECS Fargate | Containers without managing nodes              | **The sweet spot for most .NET services**                             |
| **Kubernetes**                               | Many services, a platform team                 | A role, not a task — be honest about who owns it                      |
| **Azure Functions** / Lambda                 | Event-driven, spiky, scheduled                 | Cold starts; connection exhaustion                                    |
| **VMs**                                      | Lift and shift; licensing tied to cores        | You own patching and configuration                                    |
| **IIS on Windows Server**                    | **.NET Framework**, and it works               | Modernise operations before porting                                   |
| **Windows containers**                       | Framework apps needing container orchestration | Multi-GB images; Windows node pools; rarely worth it as a destination |

``` text
  the realistic path for a .NET Framework estate, in order of effort

  1  KEEP IIS, IMPROVE OPERATIONS
       configuration as code, a pipeline instead of manual copies,
       structured logs shipped off the box, real monitoring,
       no silent recycles masking a leak
       -> WEEKS. Most of the operational pain, gone. No porting.

  2  IIS BEHIND A MODERN EDGE
       cloud load balancer terminating TLS, WAF at the edge.
       IIS becomes an origin rather than the front door.

  3  NEW WORK IN MODERN .NET, BEHIND A PROXY
       YARP routes /orders to a new container; everything else to IIS.
       both share the database. Move one route at a time.

  4  PORT THE REST
       leaves first, following the dependency graph. The shell last.

  step 1 is where the value is, and it is the step teams skip because
  it is not a migration.
```

The honest framing: .NET Framework 4.8 is supported for as long as the Windows it ships with, so there is no deadline forcing a port. Migrate for *benefit* — performance, Linux hosting cost, the modern ecosystem, hiring — and be willing to say that a stable internal system under no active development is a legitimate thing to leave alone.
