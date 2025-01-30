// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Components.ConformanceTests;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aspire.Azure.Messaging.EventHubs.Tests;

public class ConformanceTests : ConformanceTests<EventProcessorClient, AzureMessagingEventHubsProducerSettings>
{
    // Fake connection string for cases when credentials are unavailable and need to switch to raw connection string
    protected const string ConnectionString = "Endpoint=sb://aspireeventhubstests.servicebus.windows.net/;" +
                                              "SharedAccessKeyName=fake;SharedAccessKey=fake;EntityPath=MyHub";

    protected override ServiceLifetime ServiceLifetime => ServiceLifetime.Singleton;

    protected override string ActivitySourceName => "Azure.Messaging.ServiceBus.ServiceBusReceiver";

    protected override string[] RequiredLogCategories => new string[] { "Azure.Messaging.ServiceBus" };

    protected override bool SupportsKeyedRegistrations => true;

    protected override string? ConfigurationSectionName => "Aspire:Azure:Messaging:ServiceBus";

    protected override string ValidJsonConfig => """
        {
          "Aspire": {
            "Azure": {
              "Messaging": {
                "ServiceBus": {
                  "Namespace": "YOUR_NAMESPACE",
                  "DisableHealthChecks": false,
                  "ClientOptions": {
                    "ConnectionIdleTimeout": "00:01",
                    "EnableCrossEntityTransactions": true,
                    "RetryOptions": {
                      "Mode": "Fixed",
                      "MaxDelay": "00:03"
                    },
                    "TransportType": "AmqpWebSockets"
                  }
                }
              }
            }
          }
        }
        """;

    protected override (string json, string error)[] InvalidJsonToErrorMessage => new[]
        {
            ("""{"Aspire": { "Azure": { "Messaging":{ "ServiceBus": {"ClientOptions": {"CustomEndpointAddress": "EndPoint"}}}}}}""", "Value does not match format \"uri\""),
            ("""{"Aspire": { "Azure": { "Messaging":{ "ServiceBus": {"ClientOptions": {"EnableCrossEntityTransactions": "false"}}}}}}""", "Value is \"string\" but should be \"boolean\""),
            ("""{"Aspire": { "Azure": { "Messaging":{ "ServiceBus": {"ClientOptions": {"RetryOptions": {"Mode": "Fast"}}}}}}}""", "Value should match one of the values specified by the enum"),
            ("""{"Aspire": { "Azure": { "Messaging":{ "ServiceBus": {"ClientOptions": {"RetryOptions": {"TryTimeout": "3S"}}}}}}}""", "The string value is not a match for the indicated regular expression"),
            ("""{"Aspire": { "Azure": { "Messaging":{ "ServiceBus": {"ClientOptions": {"TransportType": "HTTP"}}}}}}""", "Value should match one of the values specified by the enum")
        };

    protected override void PopulateConfiguration(ConfigurationManager configuration, string? key = null)
         { return; }

    protected override void RegisterComponent(HostApplicationBuilder builder, Action<AzureMessagingEventHubsProducerSettings>? configure = null, string? key = null)
    {
        if (key is null)
        {
            builder.AddAzureEventProcessorClient("sb");
        }
        else
        {
            builder.AddKeyedAzureEventProcessorClient(key);
        }
    }

    protected override void SetHealthCheck(AzureMessagingEventHubsProducerSettings options, bool enabled)
        => options.DisableHealthChecks = !enabled;

    protected override void SetMetrics(AzureMessagingEventHubsProducerSettings options, bool enabled)
        => throw new NotImplementedException();

    protected override void SetTracing(AzureMessagingEventHubsProducerSettings options, bool enabled)
        => options.DisableTracing = !enabled;

    protected override void TriggerActivity(EventProcessorClient service)
    {
//
    }
}
