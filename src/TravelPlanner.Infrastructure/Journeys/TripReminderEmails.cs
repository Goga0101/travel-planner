using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Features.Journeys;

namespace TravelPlanner.Infrastructure.Journeys;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string BaseUrl { get; set; } = "https://api.brevo.com";
    /// <summary>Brevo API key (brevo.com → SMTP &amp; API → API keys). Without it no e-mail is sent.</summary>
    public string? BrevoApiKey { get; set; }
    /// <summary>Sender address; Brevo sends only from an address verified in the Brevo account.</summary>
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "Tripora";

    public bool IsConfigured => IsSet(BrevoApiKey) && IsSet(FromAddress);
    private static bool IsSet(string? v) => !string.IsNullOrWhiteSpace(v) && !v.StartsWith('<');
}

/// <summary>Transactional e-mail through Brevo's HTTP API (POST /v3/smtp/email).</summary>
public sealed class BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options) : IEmailSender
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = o.FromName, email = o.FromAddress },
                to = new[] { new { email = message.ToAddress, name = message.ToName } },
                subject = message.Subject,
                htmlContent = message.Html,
                textContent = message.Text,
            }),
        };
        request.Headers.Add("api-key", o.BrevoApiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>No e-mail service configured: reminders stay pending (and go out once a key is added, if still due).</summary>
public sealed class NoEmailSender : IEmailSender
{
    public bool IsConfigured => false;
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Every few minutes, sends the trip reminder e-mails that are due.</summary>
public sealed class TripReminderWorker(IServiceScopeFactory scopes, IOptions<TripReminderOptions> options, ILogger<TripReminderWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.CheckEveryMinutes));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var sent = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new SendTripRemindersCommand(), stoppingToken);
                    if (sent > 0) logger.LogInformation("Sent {Count} trip reminder e-mails", sent);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("Trip reminders round failed ({Error})", ex.GetType().Name);
                }
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
