using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using TravelPlanner.Application.Features.Auth;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Infrastructure.Auth;
using TravelPlanner.Infrastructure.Persistence;

namespace TravelPlanner.UnitTests;

public class AuthTests
{
    private static readonly AuthOptions Options = new()
    {
        Jwt = new AuthOptions.JwtSettings { Key = Convert.ToBase64String(Enumerable.Range(1, 48).Select(i => (byte)i).ToArray()) },
    };

    private sealed class FakeVerifier(string provider, ExternalIdentity? identity) : IExternalIdentityVerifier
    {
        public string Provider => provider;
        public bool IsConfigured => true;
        public string? PublicClientId => "client";
        public Task<ExternalIdentity?> VerifyAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(token == "good" ? identity : null);
    }

    private static AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ExternalLoginHandler Handler(AppDbContext db, params IExternalIdentityVerifier[] verifiers) =>
        new(db, verifiers, new JwtAccessTokenIssuer(Microsoft.Extensions.Options.Options.Create(Options), TimeProvider.System), TimeProvider.System);

    [Fact]
    public async Task First_Sign_In_Creates_The_User_And_A_Token_The_Api_Accepts()
    {
        await using var db = Db();
        var google = new FakeVerifier(AuthProviders.Google, new("Google", "g-1", "Nino", "nino@example.com", true, "https://pic"));

        var result = await Handler(db, google).Handle(new ExternalLoginCommand("google", "good"), CancellationToken.None);

        Assert.Equal("Nino", result.User.Name);
        Assert.Equal(["Google"], result.User.Providers);
        Assert.Single(db.Users);
        var check = await new JsonWebTokenHandler().ValidateTokenAsync(result.AccessToken, JwtAccessTokenIssuer.ValidationParameters(Options.Jwt));
        Assert.True(check.IsValid);
        Assert.Equal(result.User.Id.ToString(), check.Claims["sub"]);
    }

    [Fact]
    public async Task Signing_In_Again_Reuses_The_Account_And_Facebook_With_The_Same_Email_Is_Linked()
    {
        await using var db = Db();
        var google = new FakeVerifier(AuthProviders.Google, new("Google", "g-1", "Nino", "nino@example.com", true, null));
        var facebook = new FakeVerifier(AuthProviders.Facebook, new("Facebook", "fb-9", "Nino B.", "nino@example.com", true, "https://fb-pic"));
        var handler = Handler(db, google, facebook);

        var first = await handler.Handle(new ExternalLoginCommand("Google", "good"), CancellationToken.None);
        var again = await handler.Handle(new ExternalLoginCommand("Google", "good"), CancellationToken.None);
        var viaFacebook = await handler.Handle(new ExternalLoginCommand("Facebook", "good"), CancellationToken.None);

        Assert.Equal(first.User.Id, again.User.Id);
        Assert.Equal(first.User.Id, viaFacebook.User.Id);
        Assert.Equal(["Facebook", "Google"], viaFacebook.User.Providers);
        Assert.Equal("https://fb-pic", viaFacebook.User.PictureUrl);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task An_Unverified_Email_Does_Not_Take_Over_Another_Account()
    {
        await using var db = Db();
        var google = new FakeVerifier(AuthProviders.Google, new("Google", "g-1", "Nino", "nino@example.com", true, null));
        var facebook = new FakeVerifier(AuthProviders.Facebook, new("Facebook", "fb-2", "Someone", "nino@example.com", false, null));
        var handler = Handler(db, google, facebook);

        var a = await handler.Handle(new ExternalLoginCommand("Google", "good"), CancellationToken.None);
        var b = await handler.Handle(new ExternalLoginCommand("Facebook", "good"), CancellationToken.None);

        Assert.NotEqual(a.User.Id, b.User.Id);
        Assert.Null(b.User.Email);
    }

    [Fact]
    public async Task A_Rejected_Token_Or_A_Provider_That_Is_Off_Fails()
    {
        await using var db = Db();
        var google = new FakeVerifier(AuthProviders.Google, new("Google", "g-1", "Nino", null, false, null));

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler(db, google).Handle(new ExternalLoginCommand("Google", "forged"), CancellationToken.None));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            Handler(db, google).Handle(new ExternalLoginCommand("Facebook", "good"), CancellationToken.None));
        Assert.Empty(db.Users);
    }

    [Fact]
    public void A_Short_Or_Missing_Signing_Key_Switches_Sign_In_Off()
    {
        Assert.Null(JwtAccessTokenIssuer.SigningKey(new AuthOptions.JwtSettings { Key = "short" }));
        Assert.Null(JwtAccessTokenIssuer.SigningKey(new AuthOptions.JwtSettings { Key = "<user-secrets: Auth:Jwt:Key>" }));
        Assert.False(new JwtAccessTokenIssuer(Microsoft.Extensions.Options.Options.Create(new AuthOptions()), TimeProvider.System).IsConfigured);
    }
}
