# What is Aspire?

**.NET Aspire** is an opinionated, cloud-ready stack for building observable, production-ready distributed applications with .NET.

## Key Features

**App Model**: At the center of Aspire is the app model—a code-first, single source of truth that defines your application's services, resources, and their connections. This creates an explicit graph of your distributed system's architecture.

**Unified Toolchain**: Aspire provides a consistent development and deployment experience. Launch and debug your entire distributed application locally with one command, then deploy anywhere—Kubernetes, the cloud, or your own servers—using the same composition.

**Observability**: Built-in support for logging, metrics, and distributed tracing helps you understand how your distributed application behaves in development and production.

## Core Concepts

### Resources and Dependencies

Aspire models distributed applications as a graph of **resources**—services, infrastructure elements, and supporting components. You compose applications using fluent extension methods:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddPostgres("pg");
var api = builder.AddProject<Projects.MyApi>("api")
    .WithReference(db);
var web = builder.AddProject<Projects.MyWeb>("web")
    .WithReference(api);

builder.Build().Run();
```

### Service Discovery and Configuration

Aspire automatically handles:
- Port allocation and service discovery
- Environment variable wiring between services
- Connection string management
- Health checks and startup ordering

### Integration Ecosystem

Aspire includes integrations for popular technologies:
- Databases (PostgreSQL, SQL Server, MongoDB, Redis, etc.)
- Message brokers (RabbitMQ, Azure Service Bus, etc.)  
- Cloud services (Azure, AWS services)
- Observability tools (OpenTelemetry, Seq, etc.)

## What's in this Repository

This repository contains:
- **Aspire.Hosting**: Core abstractions for the application model
- **Aspire Dashboard**: Web-based dashboard for monitoring distributed applications
- **Service Discovery**: Infrastructure for service-to-service communication
- **Integrations**: Client libraries for popular services and infrastructure
- **Project Templates**: Templates for creating new Aspire applications

## Learn More

- [Aspire documentation](https://learn.microsoft.com/dotnet/aspire/)
- [Aspire samples repository](https://github.com/dotnet/aspire-samples)
- [App Model specification](docs/specs/appmodel.md)
- [Contributing guidelines](docs/contributing.md)