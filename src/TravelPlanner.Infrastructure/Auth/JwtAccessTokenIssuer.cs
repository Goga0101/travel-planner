using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TravelPlanner.Application.Features.Auth;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Infrastructure.Auth;

/// <summary>This API's own token: a signed JWT with the user id as "sub", checked by the API's JWT bearer authentication.</summary>
public sealed class JwtAccessTokenIssuer(IOptions<AuthOptions> options, TimeProvider time) : IAccessTokenIssuer
{
    private readonly AuthOptions.JwtSettings _jwt = options.Value.Jwt;

    public bool IsConfigured => SigningKey(_jwt) is not null;

    public IssuedToken Issue(User user)
    {
        var key = SigningKey(_jwt) ?? throw new InvalidOperationException("Auth:Jwt:Key is not configured.");
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.AddDays(_jwt.LifetimeDays);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Name, user.Name),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
        return new IssuedToken(token, expires);
    }

    /// <summary>The same parameters the API validates incoming tokens with.</summary>
    public static TokenValidationParameters ValidationParameters(AuthOptions.JwtSettings jwt) => new()
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = SigningKey(jwt),
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = JwtRegisteredClaimNames.Name,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
    };

    /// <summary>Null (sign-in off) when the key is missing or shorter than 256 bits.</summary>
    public static SymmetricSecurityKey? SigningKey(AuthOptions.JwtSettings jwt)
    {
        if (!AuthOptions.IsSet(jwt.Key)) return null;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(jwt.Key!); }
        catch (FormatException) { bytes = Encoding.UTF8.GetBytes(jwt.Key!); }
        return bytes.Length >= 32 ? new SymmetricSecurityKey(bytes) : null;
    }
}
