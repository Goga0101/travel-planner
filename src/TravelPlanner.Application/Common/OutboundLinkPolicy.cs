using Microsoft.Extensions.Options;

namespace TravelPlanner.Application.Common;

public interface IOutboundLinkPolicy
{
    /// <summary>Returns the URL if it is an absolute HTTPS URL on an allow-listed host; otherwise null.</summary>
    string? Sanitize(string? url);
}

/// <summary>
/// Only provider-supplied or curated URLs on allow-listed hosts reach the client.
/// The server never fetches these URLs, and users cannot supply outbound URLs, which prevents SSRF and open redirects.
/// </summary>
public sealed class OutboundLinkPolicy(IOptions<OutboundLinkOptions> options) : IOutboundLinkPolicy
{
    private readonly string[] _allowedHosts = options.Value.AllowedHosts
        .Where(h => !string.IsNullOrWhiteSpace(h))
        .Select(h => h.Trim().TrimStart('.').ToLowerInvariant())
        .ToArray();

    public string? Sanitize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
        if (!uri.IsDefaultPort) return null;

        var host = uri.IdnHost.ToLowerInvariant();
        var allowed = _allowedHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
        return allowed ? uri.AbsoluteUri : null;
    }
}
