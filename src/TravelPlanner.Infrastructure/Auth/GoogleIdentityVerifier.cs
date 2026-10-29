using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TravelPlanner.Application.Features.Auth;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Infrastructure.Auth;

/// <summary>
/// Google sign-in: the browser gets an ID token (a JWT signed by Google) from Google Identity Services. It is checked against
/// Google's published keys, its issuer, its expiry and its audience (our client id), so only tokens issued to this site count.
/// </summary>
public sealed class GoogleIdentityVerifier : IExternalIdentityVerifier
{
    private static readonly string[] Issuers = ["accounts.google.com", "https://accounts.google.com"];

    private readonly string? _clientId;
    private readonly ILogger<GoogleIdentityVerifier> _logger;
    // Google rotates its keys; the manager downloads and caches them (refreshing about daily or on an unknown key id).
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _google;

    public GoogleIdentityVerifier(IOptions<AuthOptions> options, IHttpClientFactory httpClients, ILogger<GoogleIdentityVerifier> logger)
    {
        _clientId = AuthOptions.IsSet(options.Value.Google.ClientId) ? options.Value.Google.ClientId : null;
        _logger = logger;
        _google = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(httpClients.CreateClient(nameof(GoogleIdentityVerifier))) { RequireHttps = true });
    }

    public string Provider => AuthProviders.Google;
    public bool IsConfigured => _clientId is not null;
    public string? PublicClientId => _clientId;

    public async Task<ExternalIdentity?> VerifyAsync(string token, CancellationToken cancellationToken)
    {
        if (_clientId is null) return null;
        var config = await _google.GetConfigurationAsync(cancellationToken);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuers = Issuers,
            ValidAudience = _clientId,
            IssuerSigningKeys = config.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        });
        if (!result.IsValid)
        {
            _logger.LogInformation("Google ID token rejected: {Reason}", result.Exception?.Message);
            return null;
        }

        string? Claim(string type) => result.Claims.TryGetValue(type, out var v) ? v?.ToString() : null;
        var sub = Claim("sub");
        if (string.IsNullOrEmpty(sub)) return null;
        var email = Claim("email");
        var verified = string.Equals(Claim("email_verified"), "true", StringComparison.OrdinalIgnoreCase);
        var name = Claim("name") ?? Claim("given_name") ?? email?.Split('@')[0] ?? "Google user";
        return new ExternalIdentity(Provider, sub, name, email, verified, Claim("picture"));
    }
}
