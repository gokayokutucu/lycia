// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lycia.Extensions.AspNetCore.Tests;

/// <summary>
/// A minimal standard <see cref="AuthenticationHandler{TOptions}"/> for proving that
/// <c>MapLyciaDiagnostics().RequireAuthorization(...)</c> composes with ordinary ASP.NET Core
/// authorization - Lycia implements no authentication or authorization of its own. Authenticates a
/// request that carries the <c>X-Test-Role</c> header, and leaves the request unauthenticated otherwise.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string RoleHeader = "X-Test-Role";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RoleHeader, out var role) || string.IsNullOrEmpty(role))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, role!)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
