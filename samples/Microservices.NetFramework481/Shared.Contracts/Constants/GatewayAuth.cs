namespace Shared.Contracts.Constants;

/// <summary>
/// Internal gateway authentication constants.
/// Used to ensure API endpoints are only accessible through the API Gateway.
/// </summary>
public static class GatewayAuth
{
    /// <summary>
    /// Header name for internal gateway authentication.
    /// </summary>
    public const string HeaderName = "X-Internal-Gateway-Key";

    /// <summary>
    /// Secret key for internal gateway authentication.
    /// In production, this should be stored in secure configuration (Azure Key Vault, etc.)
    /// </summary>
    public const string SecretKey = "MySecretGatewayKey_DO_NOT_EXPOSE_2024";
}
