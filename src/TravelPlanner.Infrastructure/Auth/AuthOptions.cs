namespace TravelPlanner.Infrastructure.Auth;

/// <summary>
/// Sign-in with Google and Facebook. Secrets (Jwt:Key, Facebook:AppSecret) belong in user-secrets or environment variables,
/// never in appsettings: e.g. <c>dotnet user-secrets set "Auth:Facebook:AppSecret" "…" --project src/TravelPlanner.API</c>.
/// A provider without its ids, or everything without Jwt:Key, is simply switched off.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public JwtSettings Jwt { get; set; } = new();
    public GoogleSettings Google { get; set; } = new();
    public FacebookSettings Facebook { get; set; } = new();

    public sealed class JwtSettings
    {
        /// <summary>HMAC-SHA256 signing key, at least 32 random bytes (base64 or plain text of 32+ characters).</summary>
        public string? Key { get; set; }
        public string Issuer { get; set; } = "travel-planner-api";
        public string Audience { get; set; } = "travel-planner-web";
        /// <summary>How long a sign-in lasts before the visitor signs in again.</summary>
        public int LifetimeDays { get; set; } = 30;
    }

    public sealed class GoogleSettings
    {
        /// <summary>OAuth client id ("….apps.googleusercontent.com"); public, the browser needs it too.</summary>
        public string? ClientId { get; set; }
    }

    public sealed class FacebookSettings
    {
        /// <summary>App id; public, the browser needs it too.</summary>
        public string? AppId { get; set; }
        /// <summary>App secret: only on the server, used to confirm a token was issued to this app.</summary>
        public string? AppSecret { get; set; }
        public string GraphVersion { get; set; } = "v23.0";
    }

    internal static bool IsSet(string? value) => !string.IsNullOrWhiteSpace(value) && !value.StartsWith('<');
}
