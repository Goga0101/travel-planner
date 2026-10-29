using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Features.Auth;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Infrastructure.Auth;

/// <summary>
/// Facebook sign-in: the browser gets a user access token from the JS SDK. Graph API's debug_token (asked with the app's own
/// token) confirms it is valid and was issued to our app; only then is the profile read, with appsecret_proof.
/// </summary>
public sealed class FacebookIdentityVerifier(HttpClient http, IOptions<AuthOptions> options, ILogger<FacebookIdentityVerifier> logger)
    : IExternalIdentityVerifier
{
    private readonly AuthOptions.FacebookSettings _fb = options.Value.Facebook;

    public string Provider => AuthProviders.Facebook;
    public bool IsConfigured => AuthOptions.IsSet(_fb.AppId) && AuthOptions.IsSet(_fb.AppSecret);
    public string? PublicClientId => IsConfigured ? _fb.AppId : null;

    public async Task<ExternalIdentity?> VerifyAsync(string token, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;
        var graph = $"https://graph.facebook.com/{_fb.GraphVersion}";

        var debug = await GetAsync<DebugResponse>(
            $"{graph}/debug_token?input_token={Uri.EscapeDataString(token)}", $"{_fb.AppId}|{_fb.AppSecret}",
            cancellationToken);
        var data = debug?.Data;
        if (data is not { IsValid: true } || data.AppId != _fb.AppId || string.IsNullOrEmpty(data.UserId))
        {
            logger.LogInformation("Facebook token rejected: valid={Valid}, for our app={OurApp}", data?.IsValid, data?.AppId == _fb.AppId);
            return null;
        }

        var proof = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_fb.AppSecret!), Encoding.UTF8.GetBytes(token)));
        var me = await GetAsync<MeResponse>(
            $"{graph}/me?fields=id,name,email,picture.width(200).height(200)&appsecret_proof={proof}", token,
            cancellationToken);
        if (me is null || me.Id != data.UserId) return null;

        // Facebook only returns an email the person has confirmed with Facebook.
        return new ExternalIdentity(Provider, me.Id, me.Name ?? "Facebook user", me.Email, me.Email is not null,
            me.Picture?.Data is { IsSilhouette: false, Url: { } url } ? url : null);
    }

    /// <param name="bearer">Sent as a header, not in the URL, so neither the app secret nor the visitor's token reaches request logs.</param>
    private async Task<TResult?> GetAsync<TResult>(string url, string bearer, CancellationToken ct) where TResult : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogInformation("Facebook Graph API returned {Status}", (int)response.StatusCode);
            return null;
        }
        return await response.Content.ReadFromJsonAsync<TResult>(ct);
    }

    private sealed record DebugResponse([property: JsonPropertyName("data")] DebugData? Data);

    private sealed record DebugData(
        [property: JsonPropertyName("app_id")] string? AppId,
        [property: JsonPropertyName("is_valid")] bool IsValid,
        [property: JsonPropertyName("user_id")] string? UserId);

    private sealed record MeResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("picture")] PictureWrapper? Picture);

    private sealed record PictureWrapper([property: JsonPropertyName("data")] PictureData? Data);

    private sealed record PictureData(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("is_silhouette")] bool IsSilhouette);
}
