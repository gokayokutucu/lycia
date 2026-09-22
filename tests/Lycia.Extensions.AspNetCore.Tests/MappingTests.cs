// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Net;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Extensions.AspNetCore.Tests;

/// <summary>Proves MapLyciaDiagnostics' mapping behavior: default route, custom route, opt-in, and verb handling.</summary>
public class MappingTests
{
    private static void RegisterStubDiagnostics(IServiceCollection services) =>
        services.AddSingleton<ILyciaReliabilityDiagnostics>(new StubDiagnostics(new LyciaReliabilitySnapshot()));

    [Fact]
    public async Task MapLyciaDiagnostics_Without_A_Path_Maps_The_Default_Route()
    {
        using var host = await TestHost.StartAsync(
            RegisterStubDiagnostics,
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var response = await host.Client().GetAsync("/diagnostics/lycia");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MapLyciaDiagnostics_With_A_Custom_Path_Maps_Only_That_Path()
    {
        using var host = await TestHost.StartAsync(
            RegisterStubDiagnostics,
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics("/internal/lycia"));
            });

        var custom = await host.Client().GetAsync("/internal/lycia");
        var defaultRoute = await host.Client().GetAsync("/diagnostics/lycia");

        Assert.Equal(HttpStatusCode.OK, custom.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, defaultRoute.StatusCode);
    }

    [Fact]
    public async Task An_Application_That_Never_Calls_MapLyciaDiagnostics_Exposes_No_Diagnostics_Route()
    {
        using var host = await TestHost.StartAsync(
            RegisterStubDiagnostics,
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(_ => { }); // no MapLyciaDiagnostics() call
            });

        var response = await host.Client().GetAsync("/diagnostics/lycia");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Registering_ILyciaReliabilityDiagnostics_Alone_Does_Not_Map_A_Route()
    {
        // AddLycia(...) (which registers ILyciaReliabilityDiagnostics) must never expose an HTTP endpoint
        // on its own; only an explicit MapLyciaDiagnostics() call does.
        using var host = await TestHost.StartAsync(
            RegisterStubDiagnostics,
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(_ => { });
            });

        var response = await host.Client().GetAsync("/diagnostics/lycia");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_Unsupported_Http_Verb_Is_Rejected_Like_Any_Other_Minimal_Api_Get_Endpoint()
    {
        using var host = await TestHost.StartAsync(
            RegisterStubDiagnostics,
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var response = await host.Client().PostAsync("/diagnostics/lycia", content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public void MapLyciaDiagnostics_Returns_A_Builder_That_Composes_With_Further_Conventions()
    {
        var appBuilder = WebApplication.CreateBuilder();
        RegisterStubDiagnostics(appBuilder.Services);
        var app = appBuilder.Build();

        var builder = app.MapLyciaDiagnostics();

        Assert.NotNull(builder);
        // The composition itself (RequireAuthorization) is proven end-to-end in CompositionTests; this
        // only proves the return type supports further IEndpointConventionBuilder chaining, exactly as
        // the public API documents (app.MapLyciaDiagnostics().RequireAuthorization("Operations")).
        builder.WithName("AnotherName");
    }
}
