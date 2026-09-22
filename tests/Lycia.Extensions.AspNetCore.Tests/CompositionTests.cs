// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Net;
using Lycia.Saga.Abstractions.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Extensions.AspNetCore.Tests;

/// <summary>
/// Proves <c>app.MapLyciaDiagnostics().RequireAuthorization(...)</c> composes with, and is actually
/// enforced by, standard ASP.NET Core authentication/authorization - not a Lycia-specific mechanism.
/// </summary>
public class CompositionTests
{
    private static async Task<IHost> StartProtectedHostAsync()
    {
        return await TestHost.StartAsync(
            services =>
            {
                services.AddSingleton<ILyciaReliabilityDiagnostics>(new StubDiagnostics(new LyciaReliabilitySnapshot()));
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.AddAuthorization(options =>
                    options.AddPolicy("Operations", policy => policy.RequireRole("Operations")));
            },
            app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                    endpoints.MapLyciaDiagnostics().RequireAuthorization("Operations"));
            });
    }

    [Fact]
    public async Task RequireAuthorization_Returns_A_Builder_The_Caller_Can_Keep_Composing()
    {
        using var host = await StartProtectedHostAsync();
        // Constructing the host already proves RequireAuthorization("Operations") type-checks against the
        // builder MapLyciaDiagnostics() returns; this asserts the server actually started successfully.
        Assert.NotNull(host);
    }

    [Fact]
    public async Task An_Unauthenticated_Request_Is_Rejected_By_Standard_ASPNET_Core_Authorization()
    {
        using var host = await StartProtectedHostAsync();

        var response = await host.Client().GetAsync("/diagnostics/lycia");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_Request_With_The_Wrong_Role_Is_Forbidden()
    {
        using var host = await StartProtectedHostAsync();
        var client = host.Client();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "SomeOtherRole");

        var response = await client.GetAsync("/diagnostics/lycia");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_Request_Satisfying_The_Policy_Is_Allowed_Through()
    {
        using var host = await StartProtectedHostAsync();
        var client = host.Client();
        client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, "Operations");

        var response = await client.GetAsync("/diagnostics/lycia");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Without_RequireAuthorization_The_Endpoint_Stays_Anonymous()
    {
        using var host = await TestHost.StartAsync(
            services =>
                services.AddSingleton<ILyciaReliabilityDiagnostics>(new StubDiagnostics(new LyciaReliabilitySnapshot())),
            app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapLyciaDiagnostics());
            });

        var response = await host.Client().GetAsync("/diagnostics/lycia");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
