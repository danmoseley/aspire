// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;
using Aspire.Azure.Common;
using Aspire.Azure.Messaging.EventHubs;
using Azure.Core;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using HealthChecks.Azure.Messaging.EventHubs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

internal abstract class EventHubsComponent<TSettings, TClient, TClientOptions> :
    AzureComponent<TSettings, TClient, TClientOptions>
    where TClientOptions: class
    where TClient : class
    where TSettings : AzureMessagingEventHubsSettings, new()
{
    // each EventHub client class is in a different namespace, so the base AzureComponent.ActivitySourceNames logic doesn't work
    protected override string[] ActivitySourceNames => ["Azure.Messaging.EventHubs.*"];

    protected override IHealthCheck CreateHealthCheck(TClient client, TSettings settings)
    {
        // if this is a producer client, reuse the instance.
        if (client is EventHubProducerClient producer)
        {
            return new AzureEventHubHealthCheck(producer);
        }

        // else create one
        var probeOptions = new EventHubProducerClientOptions
        {
            Identifier = $"AspireEventHubHealthCheck-{settings.EventHubName}",
        };
        var probe = !string.IsNullOrEmpty(settings.ConnectionString) ?
            new EventHubProducerClient(settings.ConnectionString, probeOptions) :
            new EventHubProducerClient(settings.FullyQualifiedNamespace, settings.EventHubName, settings.Credential, probeOptions);

        return new AzureEventHubHealthCheck(probe);
    }

    protected override bool GetHealthCheckEnabled(TSettings settings)
        => !settings.DisableHealthChecks;

    protected override TokenCredential? GetTokenCredential(TSettings settings)
        => settings.Credential;

    protected override bool GetTracingEnabled(TSettings settings)
        => !settings.DisableTracing;

    protected static string GenerateClientIdentifier(string? eventHubName, string? consumerGroup)
    {
        // configure processor identifier
        var slug = Guid.NewGuid().ToString().Substring(24);
        var identifier = $"{Environment.MachineName}-{eventHubName}-" +
                         $"{consumerGroup ?? "default"}-{slug}";

        return identifier;
    }

    protected static string GetNamespaceFromSettings(AzureMessagingEventHubsSettings settings)
    {
        string ns;

        try
        {
            // Extract the namespace from the connection string or qualified namespace
            ns = string.IsNullOrWhiteSpace(settings.FullyQualifiedNamespace)
                ? EventHubsConnectionStringProperties.Parse(settings.ConnectionString).Endpoint.Host
                : new Uri(settings.FullyQualifiedNamespace).Host;

            // This is likely to be similar to {yournamespace}.servicebus.windows.net or {yournamespace}.servicebus.chinacloudapi.cn
            if (ns.Contains(".servicebus", StringComparison.OrdinalIgnoreCase))
            {
                ns = ns[..ns.IndexOf(".servicebus")];
            }
            else
            {
                // Use a random prefix if no meaningful name is found e.g., "localhost", "127.0.0.1".
                // This is used to create blob containers names that are unique in the referenced storage account.
                RandomNumberGenerator.GetHexString(12, true);
            }
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException(
                $"A {typeof(TClient).Name} could not be configured. Please ensure that the ConnectionString or FullyQualifiedNamespace is well-formed.");
        }

        return ns;
    }

    protected static void EnsureConnectionStringOrNamespaceProvided(AzureMessagingEventHubsSettings settings,
        string connectionName, string configurationSectionName)
    {
        var connectionString = settings.ConnectionString;

        // Are we missing both connection string and namespace? throw.
        if (string.IsNullOrEmpty(connectionString) && string.IsNullOrEmpty(settings.FullyQualifiedNamespace))
        {
            throw new InvalidOperationException(
                $"A {typeof(TClient).Name} could not be configured. Ensure valid connection information was provided in " +
                $"'ConnectionStrings:{connectionName}' or specify a 'ConnectionString' or 'FullyQualifiedNamespace' in the '{configurationSectionName}' configuration section.");
        }

        // If we have a connection string, ensure there's an EntityPath if settings.EventHubName is missing
        if (!string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            // We have a connection string -- do we have an EventHubName?
            if (string.IsNullOrWhiteSpace(settings.EventHubName))
            {
                // look for EntityPath
                var props = EventHubsConnectionStringProperties.Parse(connectionString);

                // if EntityPath is missing, throw
                if (string.IsNullOrWhiteSpace(props.EventHubName))
                {
                    throw new InvalidOperationException(
                        $"A {typeof(TClient).Name} could not be configured. Ensure a valid EventHubName was provided in " +
                        $"the '{configurationSectionName}' configuration section, or include an EntityPath in the ConnectionString.");
                }
                // The connection string has an EventHubName, but we'll set this anyway so the health check can use it
                settings.EventHubName = props.EventHubName;
            }
        }
        // If we have a namespace and no connection string, ensure there's an EventHubName
        else if (!string.IsNullOrWhiteSpace(settings.FullyQualifiedNamespace) && string.IsNullOrWhiteSpace(settings.EventHubName))
        {
            throw new InvalidOperationException(
                $"A {typeof(TClient).Name} could not be configured. Ensure a valid EventHubName was provided in " +
                $"the '{configurationSectionName}' configuration section.");
        }
    }
}
