using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Application.Features.Auth;

/// <summary>Who a provider says signed in, after the provider confirmed the token was issued to this app.</summary>
public sealed record ExternalIdentity(string Provider, string ProviderUserId, string Name, string? Email, bool EmailVerified, string? PictureUrl);

/// <summary>
/// Checks a token the browser got from a provider (Google ID token, Facebook access token): signature or provider check,
/// and that it was issued to this app (audience / app id), so a token for another site cannot be replayed here.
/// </summary>
public interface IExternalIdentityVerifier
{
    string Provider { get; }
    bool IsConfigured { get; }
    /// <summary>The public id the browser needs (Google client id, Facebook app id); null when not configured.</summary>
    string? PublicClientId { get; }
    /// <returns>Null when the token is invalid, expired or issued to another app.</returns>
    Task<ExternalIdentity?> VerifyAsync(string token, CancellationToken cancellationToken);
}

public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>Issues this API's own bearer token for a signed-in user.</summary>
public interface IAccessTokenIssuer
{
    bool IsConfigured { get; }
    IssuedToken Issue(User user);
}

public sealed record UserDto(Guid Id, string Name, string? Email, string? PictureUrl, IReadOnlyList<string> Providers, DateTime CreatedAtUtc);

public sealed record AuthResultDto(string AccessToken, DateTime ExpiresAtUtc, UserDto User);

/// <summary>The provider token was rejected (invalid, expired, for another app) or sign-in is not set up.</summary>
public sealed class AuthenticationFailedException(string message) : Exception(message);

/// <param name="Provider">"Google" or "Facebook".</param>
/// <param name="Token">Google: the ID token (credential) from Google Identity Services. Facebook: the user access token from the JS SDK.</param>
public sealed record ExternalLoginCommand(string Provider, string Token) : IRequest<AuthResultDto>;

public sealed class ExternalLoginValidator : AbstractValidator<ExternalLoginCommand>
{
    public ExternalLoginValidator()
    {
        RuleFor(x => x.Provider).Must(p => AuthProviders.All.Contains(p, StringComparer.OrdinalIgnoreCase))
            .WithMessage(_ => T.Get("auth.unknownProvider"));
        RuleFor(x => x.Token).NotEmpty().MaximumLength(8192);
    }
}

public sealed class ExternalLoginHandler(
    IApplicationDbContext db,
    IEnumerable<IExternalIdentityVerifier> verifiers,
    IAccessTokenIssuer tokens,
    TimeProvider time)
    : IRequestHandler<ExternalLoginCommand, AuthResultDto>
{
    public async Task<AuthResultDto> Handle(ExternalLoginCommand request, CancellationToken cancellationToken)
    {
        var verifier = verifiers.FirstOrDefault(v => v.Provider.Equals(request.Provider, StringComparison.OrdinalIgnoreCase));
        if (verifier is not { IsConfigured: true } || !tokens.IsConfigured)
            throw new AuthenticationFailedException(T.Get("auth.notConfigured"));
        var identity = await verifier.VerifyAsync(request.Token, cancellationToken)
                       ?? throw new AuthenticationFailedException(T.Get("auth.invalidToken"));

        var now = time.GetUtcNow().UtcDateTime;
        var login = await db.UserLogins.FirstOrDefaultAsync(
            l => l.Provider == identity.Provider && l.ProviderUserId == identity.ProviderUserId, cancellationToken);
        User? user = login is null ? null : await db.Users.Include(u => u.Logins).FirstOrDefaultAsync(u => u.Id == login.UserId, cancellationToken);

        // The same person on the other provider: link by an email both providers verified, so there is one account.
        if (user is null && identity is { EmailVerified: true, Email: { Length: > 0 } email })
            user = await db.Users.Include(u => u.Logins).FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            user = new User(identity.Name, identity is { EmailVerified: true } ? identity.Email : null, identity.PictureUrl, now);
            db.Users.Add(user);
        }
        else
        {
            user.LastLoginAtUtc = now;
            // Keep what the provider knows current (a new photo or name), without erasing what another provider gave.
            if (!string.IsNullOrWhiteSpace(identity.Name)) user.Name = identity.Name;
            if (identity.PictureUrl is not null) user.PictureUrl = identity.PictureUrl;
            if (user.Email is null && identity is { EmailVerified: true }) user.Email = identity.Email;
        }
        if (!user.Logins.Any(l => l.Provider == identity.Provider && l.ProviderUserId == identity.ProviderUserId))
        {
            var link = new UserLogin(user.Id, identity.Provider, identity.ProviderUserId, now);
            user.Logins.Add(link);
            db.UserLogins.Add(link);
        }
        await db.SaveChangesAsync(cancellationToken);

        var issued = tokens.Issue(user);
        return new AuthResultDto(issued.AccessToken, issued.ExpiresAtUtc, user.ToDto());
    }
}

/// <summary>The signed-in user (id from this API's own token).</summary>
public sealed record GetCurrentUserQuery(Guid UserId) : IRequest<UserDto>;

public sealed class GetCurrentUserHandler(IApplicationDbContext db) : IRequestHandler<GetCurrentUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken) =>
        (await db.Users.AsNoTracking().Include(u => u.Logins).FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken))?.ToDto()
        ?? throw new NotFoundException("resource.user", request.UserId);
}

internal static class UserMapping
{
    public static UserDto ToDto(this User u) =>
        new(u.Id, u.Name, u.Email, u.PictureUrl, u.Logins.Select(l => l.Provider).Distinct().Order().ToList(), u.CreatedAtUtc);
}

/// <summary>Deletes the signed-in user and their provider links (what the privacy page promises).</summary>
public sealed record DeleteCurrentUserCommand(Guid UserId) : IRequest;

public sealed class DeleteCurrentUserHandler(IApplicationDbContext db) : IRequestHandler<DeleteCurrentUserCommand>
{
    public async Task Handle(DeleteCurrentUserCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Users.Include(u => u.Logins).FirstOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null) return;
        db.UserLogins.RemoveRange(user.Logins);
        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
    }
}
