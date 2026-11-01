using System.Net;
using System.Net.Mail;

namespace GymApp.Server.Email;

/// <summary>Sends one plain-text email. False if it didn't go; the failure is logged, never thrown.</summary>
public interface IEmailSender
{
    Task<bool> SendAsync(string to, string subject, string body);
}

/// <summary>The <c>Email:*</c> settings (env <c>Email__Host</c> and so on). Any SMTP server with a login works, such as
/// Gmail with an app password, so moving to a mail provider is only a change of settings.</summary>
public sealed record EmailSettings(string Host, int Port, string User, string Password, string From)
{
    public const string SectionName = "Email";
    public const int DefaultPort = 587;

    /// <summary>The settings, null when <c>Email:Host</c> is unset. Throws when they're only half there, so a typo stops
    /// startup instead of every email quietly failing.</summary>
    public static EmailSettings? Read(IConfiguration config)
    {
        var s = config.GetSection(SectionName);
        if (string.IsNullOrWhiteSpace(s["Host"])) return null;
        var user = s["User"];
        var password = s["Password"];
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Email__Host is set, so set Email__User and Email__Password too.");
        return new EmailSettings(s["Host"]!.Trim(), s.GetValue("Port", DefaultPort), user.Trim(), password, s["From"]?.Trim() ?? user.Trim());
    }
}

/// <summary>Sends through SMTP with STARTTLS. Inside the request, for the same reason as Discord forwarding: a host that
/// scales to zero only runs the app while a request is open.</summary>
public sealed class SmtpEmailSender(EmailSettings settings, ILogger<SmtpEmailSender> log) : IEmailSender
{
    /// <summary>Shorter than the app's 10-second timeout, so a slow mail server can't look like being offline.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    public async Task<bool> SendAsync(string to, string subject, string body)
    {
        try
        {
            using var message = new MailMessage(new MailAddress(settings.From, "GainStreak"), new MailAddress(to)) { Subject = subject, Body = body };
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(settings.User, settings.Password)
            };
            // SmtpClient.Timeout only covers its synchronous Send
            using var timeout = new CancellationTokenSource(Timeout);
            await client.SendMailAsync(message, timeout.Token);
            return true;
        }
        catch (Exception e) when (e is SmtpException or InvalidOperationException or FormatException or OperationCanceledException)
        {
            // Not the address: this log shouldn't hold who it was for
            log.LogError(e, "Sending an email failed.");
            return false;
        }
    }
}

/// <summary>Development with no settings: the email goes to the server's log, code included.</summary>
public sealed class LogEmailSender(ILogger<LogEmailSender> log) : IEmailSender
{
    public Task<bool> SendAsync(string to, string subject, string body)
    {
        log.LogInformation("Email to {To}: {Subject}\n{Body}", to, subject, body);
        return Task.FromResult(true);
    }
}

/// <summary>Production with no settings: accounts work, but nothing can be sent. Logged, so the host's logs say why.</summary>
public sealed class NoEmailSender(ILogger<NoEmailSender> log) : IEmailSender
{
    public Task<bool> SendAsync(string to, string subject, string body)
    {
        log.LogWarning("An email wasn't sent: Email__Host isn't set.");
        return Task.FromResult(false);
    }
}
