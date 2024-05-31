// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace Aspire.Hosting.Testing;

/// <summary>
/// Factory for creating a distributed application for testing.
/// </summary>
/// <param name="entryPoint">A type in the entry point assembly of the target Aspire AppHost. Typically, the Program class can be used.</param>
/// <param name="args">
/// The command-line arguments to pass to the entry point.
/// </param>
public class DistributedApplicationFactory(Type entryPoint, string[] args) : IDisposable, IAsyncDisposable
{
    private readonly Type _entryPoint = entryPoint ?? throw new ArgumentNullException(nameof(entryPoint));
    private readonly TaskCompletionSource _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _exitTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<DistributedApplicationBuilder> _builderTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<DistributedApplication> _appTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _lockObj = new();
    private bool _entryPointStarted;
    private IHostApplicationLifetime? _hostApplicationLifetime;

    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedApplicationFactory"/> class.
    /// </summary>
    /// <param name="entryPoint">A type in the entry point assembly of the target Aspire AppHost. Typically, the Program class can be used.</param>
    public DistributedApplicationFactory(Type entryPoint) : this(entryPoint, [])
    {
    }

    /// <summary>
    /// Gets the distributed application associated with this instance.
    /// </summary>
    internal async Task<DistributedApplicationBuilder> ResolveBuilderAsync(CancellationToken cancellationToken = default)
    {
        EnsureEntryPointStarted();
        return await _builderTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the distributed application associated with this instance.
    /// </summary>
    internal async Task<DistributedApplication> ResolveApplicationAsync(CancellationToken cancellationToken = default)
    {
        EnsureEntryPointStarted();
        return await _appTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the application.
    /// </summary>
    /// <param name="cancellationToken">A token used to signal cancellation.</param>
    /// <returns>A <see cref="Task"/> representing the completion of the operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureEntryPointStarted();
        await _startedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an instance of <see cref="HttpClient"/> that is configured to route requests to the specified resource and endpoint.
    /// </summary>
    /// <returns>The <see cref="HttpClient"/>.</returns>
    public HttpClient CreateHttpClient(string resourceName, string? endpointName = default)
    {
        return GetStartedApplication().CreateHttpClient(resourceName, endpointName);
    }

    /// <summary>
    /// Gets the connection string for the specified resource.
    /// </summary>
    /// <param name="resourceName">The resource name.</param>
    /// <returns>The connection string for the specified resource.</returns>
    /// <exception cref="ArgumentException">The resource was not found or does not expose a connection string.</exception>
    public ValueTask<string?> GetConnectionString(string resourceName)
    {
        return GetStartedApplication().GetConnectionStringAsync(resourceName);
    }

    /// <summary>
    /// Gets the endpoint for the specified resource.
    /// </summary>
    /// <param name="resourceName">The resource name.</param>
    /// <param name="endpointName">The optional endpoint name. If none are specified, the single defined endpoint is returned.</param>
    /// <returns>A URI representation of the endpoint.</returns>
    /// <exception cref="ArgumentException">The resource was not found, no matching endpoint was found, or multiple endpoints were found.</exception>
    /// <exception cref="InvalidOperationException">The resource has no endpoints.</exception>
    public Uri GetEndpoint(string resourceName, string? endpointName = default)
    {
        return GetStartedApplication().GetEndpoint(resourceName, endpointName);
    }

    /// <summary>
    /// Called when the application builder is being created.
    /// </summary>
    /// <param name="applicationOptions">The application options.</param>
    /// <param name="hostOptions">The host builder options.</param>
    protected virtual void OnBuilderCreating(DistributedApplicationOptions applicationOptions, HostApplicationBuilderSettings hostOptions)
    {
    }

    /// <summary>
    /// Called when the application builder is created.
    /// </summary>
    /// <param name="applicationBuilder">The application builder.</param>
    protected virtual void OnBuilderCreated(DistributedApplicationBuilder applicationBuilder)
    {
    }

    /// <summary>
    /// Called when the application is being built.
    /// </summary>
    /// <param name="applicationBuilder">The application builder.</param>
    protected virtual void OnBuilding(DistributedApplicationBuilder applicationBuilder)
    {
    }

    /// <summary>
    /// Called when the application has been built.
    /// </summary>
    /// <param name="application">The application.</param>
    protected virtual void OnBuilt(DistributedApplication application)
    {
    }

    private void OnBuiltCore(DistributedApplication application)
    {
        _appTcs.TrySetResult(application);
        OnBuilt(application);
    }

    private void OnBuilderCreatingCore(DistributedApplicationOptions applicationOptions, HostApplicationBuilderSettings hostBuilderOptions)
    {
        hostBuilderOptions.Args = hostBuilderOptions.Args switch
        {
            { } existing => [.. existing, .. args],
            null => args
        };

        applicationOptions.Args = applicationOptions.Args switch
        {
            { } existing => [.. existing, .. args],
            null => args
        };

        hostBuilderOptions.EnvironmentName = Environments.Development;
        hostBuilderOptions.ApplicationName = _entryPoint.Assembly.GetName().Name ?? string.Empty;
        applicationOptions.AssemblyName = _entryPoint.Assembly.GetName().Name ?? string.Empty;
        applicationOptions.DisableDashboard = true;
        var cfg = hostBuilderOptions.Configuration ??= new();
        cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DcpPublisher:RandomizePorts"] = "true",
            ["DcpPublisher:DeleteResourcesOnShutdown"] = "true",
            ["DcpPublisher:ResourceNameSuffix"] = $"{Random.Shared.Next():x}",
        });

        OnBuilderCreating(applicationOptions, hostBuilderOptions);
    }

    private void OnBuilderCreatedCore(DistributedApplicationBuilder applicationBuilder)
    {
        OnBuilderCreated(applicationBuilder);
    }

    private void OnBuildingCore(DistributedApplicationBuilder applicationBuilder)
    {
        var services = applicationBuilder.Services;
        services.AddHttpClient();

        InterceptHostCreation(applicationBuilder);

        _builderTcs.TrySetResult(applicationBuilder);
        OnBuilding(applicationBuilder);
    }

    private void EnsureEntryPointStarted()
    {
        if (!_entryPointStarted)
        {
            lock (_lockObj)
            {
                if (!_entryPointStarted)
                {
                    EnsureDepsFile(_entryPoint);

                    // This helper launches the target assembly's entry point and hooks into the lifecycle
                    // so we can intercept execution at key stages.
                    var factory = DistributedApplicationEntryPointInvoker.ResolveEntryPoint(
                        _entryPoint.Assembly,
                        onConstructing: OnBuilderCreatingCore,
                        onConstructed: OnBuilderCreatedCore,
                        onBuilding: OnBuildingCore,
                        entryPointCompleted: OnEntryPointExit);

                    if (factory is null)
                    {
                        throw new InvalidOperationException(
                            $"Could not intercept application builder instance. Ensure that {_entryPoint} is a type in an executable assembly, that the entrypoint creates an {typeof(DistributedApplicationBuilder)}, and that the resulting {typeof(DistributedApplication)} is being started.");
                    }

                    _ = InvokeEntryPoint(factory);
                    _entryPointStarted = true;
                }
            }
        }
    }

    private async Task InvokeEntryPoint(Func<string[], CancellationToken, Task<DistributedApplication>> factory)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        try
        {
            using var cts = new CancellationTokenSource(GetConfiguredTimeout());
            var app = await factory(args ?? [], cts.Token).ConfigureAwait(false);
            _hostApplicationLifetime = app.Services.GetService<IHostApplicationLifetime>()
                ?? throw new InvalidOperationException($"Application did not register an implementation of {typeof(IHostApplicationLifetime)}.");
            OnBuiltCore(app);
        }
        catch (Exception exception)
        {
            _exitTcs.TrySetException(exception);
            OnException(exception);
        }

        static TimeSpan GetConfiguredTimeout()
        {
            const string TimeoutEnvironmentKey = "DOTNET_HOST_FACTORY_RESOLVER_DEFAULT_TIMEOUT_IN_SECONDS";
            if (Debugger.IsAttached)
            {
                return Timeout.InfiniteTimeSpan;
            }

            if (uint.TryParse(Environment.GetEnvironmentVariable(TimeoutEnvironmentKey), out var timeoutInSeconds))
            {
                return TimeSpan.FromSeconds((int)timeoutInSeconds);
            }

            return TimeSpan.FromMinutes(5);
        }
    }

    private void OnEntryPointExit(Exception? exception)
    {
        if (exception is not null)
        {
            _exitTcs.TrySetException(exception);
            OnException(exception);
        }
        else
        {
            _exitTcs.TrySetResult();
        }
    }

    private void ThrowIfNotInitialized()
    {
        if (!_startedTcs.Task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("The application has not been initialized.");
        }
    }

    private static void EnsureDepsFile(Type entryPoint)
    {
        if (entryPoint.Assembly.EntryPoint == null)
        {
            throw new InvalidOperationException($"Assembly of specified type {entryPoint.Name} does not have an entry point.");
        }

        var depsFileName = $"{entryPoint.Assembly.GetName().Name}.deps.json";
        var depsFile = new FileInfo(Path.Combine(AppContext.BaseDirectory, depsFileName));
        if (!depsFile.Exists)
        {
            throw new InvalidOperationException($"Missing deps file '{Path.GetFileName(depsFile.FullName)}'. Make sure the project has been built.");
        }
    }

    private DistributedApplication GetStartedApplication()
    {
        ThrowIfNotInitialized();
        return _appTcs.Task.GetAwaiter().GetResult();
    }

    private void OnException(Exception exception)
    {
        _appTcs.TrySetException(exception);
        _builderTcs.TrySetException(exception);
        _startedTcs.TrySetException(exception);
    }

    private void OnDisposed()
    {
        _builderTcs.TrySetCanceled();
        _startedTcs.TrySetCanceled();
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        OnDisposed();
        if (_hostApplicationLifetime is null || _appTcs.Task is not { IsCompletedSuccessfully: true } appTask)
        {
            return;
        }

        _hostApplicationLifetime?.StopApplication();
        appTask.GetAwaiter().GetResult()?.Dispose();
    }

    /// <inheritdoc/>
    public virtual async ValueTask DisposeAsync()
    {
        OnDisposed();
        if (_appTcs.Task is not { IsCompletedSuccessfully: true } appTask)
        {
            _appTcs.TrySetCanceled();
            return;
        }

        if (_hostApplicationLifetime is { } hostLifetime && hostLifetime.ApplicationStarted.IsCancellationRequested)
        {
            hostLifetime.StopApplication();
        }

        await _exitTcs.Task.ConfigureAwait(false);

        if (appTask.GetAwaiter().GetResult() is { } appDisposable)
        {
            await appDisposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Replaces the IHost registration with an InterceptedHost registration which delegates to the original registration.
    private void InterceptHostCreation(DistributedApplicationBuilder applicationBuilder)
    {
        // Find the original IHost registration and remove it.
        var hostDescriptor = applicationBuilder.Services.Single(s => s.ServiceType == typeof(IHost) && s.ServiceKey is null);
        applicationBuilder.Services.Remove(hostDescriptor);

        // Insert the registration, modified to be a keyed service keyed on this factory instance.
        var interceptedDescriptor = hostDescriptor switch
        {
            { ImplementationFactory: { } factory } => ServiceDescriptor.KeyedSingleton<IHost>(this, (sp, _) => (IHost)factory(sp)),
            { ImplementationInstance: { } instance } => ServiceDescriptor.KeyedSingleton<IHost>(this, (IHost)instance),
            { ImplementationType: { } type } => ServiceDescriptor.KeyedSingleton(typeof(IHost), this, type),
            _ => throw new InvalidOperationException($"Registered service descriptor for {typeof(IHost)} does not conform to any known pattern.")
        };
        applicationBuilder.Services.Add(interceptedDescriptor);

        // Add a non-keyed registration which resolved the keyed registration, enabling interception.
        applicationBuilder.Services.AddSingleton<IHost>(sp => new ObservedHost(sp.GetRequiredKeyedService<IHost>(this), this));
    }

    private sealed class ObservedHost(IHost innerHost, DistributedApplicationFactory appFactory) : IHost, IAsyncDisposable
    {
        private bool _disposing;

        public IServiceProvider Services => innerHost.Services;

        public void Dispose()
        {
            if (_disposing)
            {
                return;
            }

            _disposing = true;
            innerHost.Dispose();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (_disposing)
            {
                return;
            }

            _disposing = true;
            if (innerHost is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                innerHost.Dispose();
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await innerHost.StartAsync(cancellationToken).ConfigureAwait(false);
                appFactory._startedTcs.TrySetResult();
            }
            catch (Exception exception)
            {
                appFactory.OnException(exception);
                throw;
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => innerHost.StopAsync(cancellationToken);
    }
}
