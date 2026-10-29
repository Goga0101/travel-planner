using TravelPlanner.Domain.Common;

namespace TravelPlanner.Domain.Entities;

/// <summary>A visitor who signed in with Google or Facebook. There are no passwords: every sign-in goes through a provider.</summary>
public class User : Entity
{
    private User() { }

    public User(string name, string? email, string? pictureUrl, DateTime nowUtc)
    {
        Name = name;
        Email = email;
        PictureUrl = pictureUrl;
        CreatedAtUtc = nowUtc;
        LastLoginAtUtc = nowUtc;
    }

    public string Name { get; set; } = null!;
    /// <summary>Verified by the provider; null when the visitor did not share it (Facebook allows that).</summary>
    public string? Email { get; set; }
    public string? PictureUrl { get; set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastLoginAtUtc { get; set; }

    public List<UserLogin> Logins { get; private set; } = [];
}

/// <summary>One provider account linked to a user: the provider's own, stable user id (Google "sub", Facebook app-scoped id).</summary>
public class UserLogin : Entity
{
    private UserLogin() { }

    public UserLogin(Guid userId, string provider, string providerUserId, DateTime nowUtc)
    {
        UserId = userId;
        Provider = provider;
        ProviderUserId = providerUserId;
        LinkedAtUtc = nowUtc;
    }

    public Guid UserId { get; private set; }
    /// <summary>One of <see cref="AuthProviders"/>.</summary>
    public string Provider { get; private set; } = null!;
    public string ProviderUserId { get; private set; } = null!;
    public DateTime LinkedAtUtc { get; private set; }
}

public static class AuthProviders
{
    public const string Google = "Google";
    public const string Facebook = "Facebook";
    public static readonly string[] All = [Google, Facebook];
}
