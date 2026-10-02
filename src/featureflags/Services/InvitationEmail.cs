using System.Text.Encodings.Web;
using FeatureFlags.Components.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FeatureFlags.Services;

public sealed class InvitationEmailOptions
{
    public string PublicBaseUrl { get; set; } = "";
    public string From { get; set; } = "";
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? PickupDirectory { get; set; }
}

public sealed class InvitationEmail(IOptions<InvitationEmailOptions> options, IWebHostEnvironment environment,
    Func<ISmtpClient>? createClient = null)
{
    public bool UsesLocalPreview => environment.IsDevelopment() && !string.IsNullOrWhiteSpace(options.Value.PickupDirectory);

    public void EnsureReady()
    {
        var settings = options.Value;
        if (!Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            Invalid("Configure the application's public HTTPS URL.");
        if (!MailboxAddress.TryParse(settings.From, out var sender) || !sender.Address.Contains('@'))
            Invalid("Configure a valid sender email address.");
        if (!environment.IsDevelopment() && !string.IsNullOrEmpty(settings.PickupDirectory))
            Invalid("Local email preview is available only in Development.");
        if (UsesLocalPreview)
        {
            try { _ = Path.GetFullPath(settings.PickupDirectory!, environment.ContentRootPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            { Invalid("Configure a valid local preview directory."); }
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.Host) || settings.Port is < 1 or > 65535 ||
            settings.Security is not (SecureSocketOptions.StartTls or SecureSocketOptions.SslOnConnect))
            Invalid("Configure SMTP host, port and required TLS.");
        if (!string.IsNullOrWhiteSpace(settings.Username) && string.IsNullOrEmpty(settings.Password))
            Invalid("Configure the SMTP password.");
    }

    private static void Invalid(string message) => throw new OptionsValidationException(
        nameof(InvitationEmailOptions), typeof(InvitationEmailOptions), [message]);

    public async Task SendAsync(IssuedInvitation issued, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureReady();
        if (issued.Token is null)
            throw new ArgumentException("The invitation was already saved. Use Resend to send a new email link.");
        var settings = options.Value;
        var baseUri = new Uri(settings.PublicBaseUrl);
        var invitation = issued.Invitation;
        var link = new Uri(baseUri, "/invitations/accept?token=" + Uri.EscapeDataString(issued.Token)).AbsoluteUri;
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(invitation.Email));
        message.Subject = $"Invitation to {invitation.ProjectName}";
        var html = HtmlEncoder.Default;
        message.Body = new BodyBuilder
        {
            TextBody = $"{invitation.InvitedBy} invited you to {invitation.ProjectName} as a {invitation.Role}.\n\nReview your invitation: {link}\n\nLog in or create an account using {invitation.Email}, then choose Join project. This invitation expires {invitation.ExpiresAt:u}. If you did not expect it, you can ignore this email.",
            HtmlBody = $"<p>{html.Encode(invitation.InvitedBy)} invited you to <strong>{html.Encode(invitation.ProjectName)}</strong> as a <strong>{invitation.Role}</strong>.</p><p><a href=\"{html.Encode(link)}\">Review invitation</a></p><p>Log in or create an account using {html.Encode(invitation.Email)}, then choose <strong>Join project</strong>.</p><p>This invitation expires {invitation.ExpiresAt:u}. If you did not expect it, you can ignore this email.</p>"
        }.ToMessageBody();
        if (UsesLocalPreview)
        {
            var directory = Path.GetFullPath(settings.PickupDirectory!, environment.ContentRootPath);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".eml");
            // Restrict invitation links to the local developer, just like other credentials.
            var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var file = new FileStream(path, fileOptions);
            await message.WriteToAsync(file, cancellationToken);
            return;
        }
        using var smtp = createClient?.Invoke() ?? new SmtpClient();
        smtp.Timeout = 15000;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await smtp.ConnectAsync(settings.Host!, settings.Port, settings.Security, timeout.Token);
        if (!string.IsNullOrWhiteSpace(settings.Username))
            await smtp.AuthenticateAsync(settings.Username, settings.Password!, timeout.Token);
        await smtp.SendAsync(message, timeout.Token);
        // The server has accepted the message; a failed QUIT must not report that delivery failed.
        try { await smtp.DisconnectAsync(true, timeout.Token); }
        catch (Exception) { }
    }
}

public sealed class InvitationDelivery(ProjectInvitations invitations, InvitationEmail email)
{
    public string SuccessMessage => email.UsesLocalPreview ? "Invitation email saved for local preview." : "Invitation email sent.";
    public async Task SendAsync(string projectId, string recipient, ProjectRole role, Guid operationId, CancellationToken cancellationToken = default)
    {
        email.EnsureReady();
        await DeliverAsync(await invitations.CreateAsync(projectId, recipient, role, operationId, cancellationToken));
    }
    public async Task ResendAsync(string projectId, int invitationId, int revision, Guid operationId, CancellationToken cancellationToken = default)
    {
        email.EnsureReady();
        await DeliverAsync(await invitations.RenewAsync(projectId, invitationId, revision, operationId, cancellationToken));
    }
    private async Task DeliverAsync(IssuedInvitation issued)
    {
        // The database has committed. Finish the bounded delivery attempt even if the caller leaves.
        try { await email.SendAsync(issued, CancellationToken.None); }
        catch (ArgumentException) when (issued.Token is null) { throw; }
        catch (Exception ex)
        {
            throw new InvitationDeliveryException("The invitation was saved, but its email could not be sent. Check email settings and use Resend to try again.", ex);
        }
    }
}

public sealed class InvitationDeliveryException(string message, Exception inner) : Exception(message, inner);
