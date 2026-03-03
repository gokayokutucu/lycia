using Shared.Contracts.Constants;

namespace Gateway.Api.Handlers;

/// <summary>
/// DelegatingHandler that adds internal gateway authentication header to all downstream requests.
/// Ensures backend microservices can validate requests are coming from the gateway.
/// </summary>
public sealed class GatewayAuthenticationHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Add internal gateway key header to downstream request
        request.Headers.Add(GatewayAuth.HeaderName, GatewayAuth.SecretKey);

        return await base.SendAsync(request, cancellationToken);
    }
}
