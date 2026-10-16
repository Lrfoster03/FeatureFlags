using FeatureFlags.Components.Models;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using System.Net;
using System.Text;

namespace FeatureFlags.Tests;

public class InvitationEmailTests
{
    [Fact]
    public async Task Local_email_contains_a_working_link_and_escapes_project_text()
    {
        await using var f = await InvitationFixture.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "invitation-mail-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var db = f.Factory.CreateDbContext();
            var project = await db.Projects.SingleAsync(); project.Name = "<b>Shared project</b>"; await db.SaveSeedChangesAsync();
            var options = Settings(directory);
            var delivery = new InvitationDelivery(f.As("owner"), new InvitationEmail(Options.Create(options), new EmailEnvironment()));
            await delivery.SendAsync(f.ProjectId, "recipient@example.com", ProjectRole.Editor, Guid.NewGuid());
            using var message = await MimeMessage.LoadAsync(Assert.Single(Directory.GetFiles(directory)));
            Assert.Equal("recipient@example.com", message.To.Mailboxes.Single().Address);
            Assert.Contains("&lt;b&gt;Shared project&lt;/b&gt;", message.HtmlBody);
            Assert.Contains("Join project", message.TextBody);
            Assert.NotNull(message.TextBody);
            var token = System.Text.RegularExpressions.Regex.Match(message.TextBody, "token=([A-F0-9]{64})").Groups[1].Value;
            Assert.Equal(f.ProjectId, (await f.As("owner").PreviewAsync(token)).ProjectId);
            Assert.Contains("http://localhost:8080/invitations/accept", message.TextBody);
            Assert.Single(await db.ProjectMembers.ToListAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Failed_delivery_keeps_invitation_and_resend_sends_a_replacement_link()
    {
        await using var f = await InvitationFixture.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "invitation-retry-" + Guid.NewGuid().ToString("N"));
        var blockedPath = Path.GetTempFileName();
        try
        {
            var settings = Settings(blockedPath);
            var delivery = new InvitationDelivery(f.As("owner"), new InvitationEmail(Options.Create(settings), new EmailEnvironment()));
            var failed = await Assert.ThrowsAsync<InvitationDeliveryException>(() => delivery.SendAsync(f.ProjectId, "recipient@example.com", ProjectRole.Viewer, Guid.NewGuid()));
            Assert.Contains("saved", failed.Message);
            await using var db = f.Factory.CreateDbContext();
            var invitation = await db.ProjectInvitations.AsNoTracking().SingleAsync();
            Assert.Null(invitation.AcceptedAt);
            settings.PickupDirectory = directory;
            f.Clock.Now = f.Clock.Now.AddMinutes(2);
            await delivery.ResendAsync(f.ProjectId, invitation.Id, invitation.Revision, Guid.NewGuid());
            Assert.Single(Directory.GetFiles(directory));
            Assert.NotEqual(invitation.TokenHash, (await db.ProjectInvitations.AsNoTracking().SingleAsync()).TokenHash);
            Assert.Equal(2, await db.AuditEvents.CountAsync());
        }
        finally
        {
            File.Delete(blockedPath);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Production_rejects_local_preview_and_untrusted_link_configuration()
    {
        await using var f = await InvitationFixture.CreateAsync();
        var issued = await f.As("owner").CreateAsync(f.ProjectId, "recipient@example.com", ProjectRole.Viewer, Guid.NewGuid());
        var settings = Settings("must-not-write");
        var sender = new InvitationEmail(Options.Create(settings), new EmailEnvironment { EnvironmentName = "Production" });
        await Assert.ThrowsAsync<OptionsValidationException>(() => sender.SendAsync(issued));
        settings.PublicBaseUrl = "https://flags.example.com";
        await Assert.ThrowsAsync<OptionsValidationException>(() => sender.SendAsync(issued));
    }

    [Theory]
    [InlineData("url")]
    [InlineData("from")]
    [InlineData("host")]
    [InlineData("port")]
    [InlineData("tls")]
    [InlineData("password")]
    [InlineData("pickup")]
    public async Task Invalid_configuration_cannot_create_or_replace_an_invitation(string invalid)
    {
        await using var f = await InvitationFixture.CreateAsync();
        var issued = await f.As("owner").CreateAsync(f.ProjectId, "recipient@example.com", ProjectRole.Viewer, Guid.NewGuid());
        f.Clock.Now = f.Clock.Now.AddMinutes(2);
        var settings = SmtpSettings();
        switch (invalid)
        {
            case "url": settings.PublicBaseUrl = "http://public.example.com"; break;
            case "from": settings.From = "invalid"; break;
            case "host": settings.Host = ""; break;
            case "port": settings.Port = 0; break;
            case "tls": settings.Security = SecureSocketOptions.None; break;
            case "password": settings.Password = null; break;
            case "pickup": settings.PickupDirectory = "local-mail"; break;
        }
        var delivery = new InvitationDelivery(f.As("owner"), new InvitationEmail(Options.Create(settings), new EmailEnvironment { EnvironmentName = "Production" }));
        await Assert.ThrowsAsync<OptionsValidationException>(() => delivery.SendAsync(f.ProjectId, "other@example.com", ProjectRole.Viewer, Guid.NewGuid()));
        await Assert.ThrowsAsync<OptionsValidationException>(() => delivery.ResendAsync(f.ProjectId, issued.Invitation.Id, 1, Guid.NewGuid()));
        Assert.Equal(issued.Invitation.Id, (await f.As("owner").PreviewAsync(issued.Token!)).Id);
        await using var db = f.Factory.CreateDbContext();
        Assert.Single(await db.ProjectInvitations.ToListAsync());
        Assert.Single(await db.AuditEvents.ToListAsync());
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("auth")]
    [InlineData("send")]
    [InlineData("disconnect")]
    [InlineData("success")]
    public async Task Smtp_failure_stages_preserve_pending_invitation_and_acceptance_is_success(string stage)
    {
        await using var f = await InvitationFixture.CreateAsync();
        var smtp = new TestSmtpClient(stage);
        var sender = new InvitationEmail(Options.Create(SmtpSettings()), new EmailEnvironment { EnvironmentName = "Production" }, () => smtp);
        var delivery = new InvitationDelivery(f.As("owner"), sender);
        if (stage is "disconnect" or "success")
        {
            await delivery.SendAsync(f.ProjectId, "recipient@example.com", ProjectRole.Viewer, Guid.NewGuid());
            Assert.True(smtp.Sent);
            Assert.True(smtp.DidDisconnect);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvitationDeliveryException>(() => delivery.SendAsync(f.ProjectId, "recipient@example.com", ProjectRole.Viewer, Guid.NewGuid()));
            Assert.IsType(stage switch
            {
                "connect" => typeof(OperationCanceledException),
                "auth" => typeof(MailKit.Security.AuthenticationException),
                _ => typeof(SmtpCommandException)
            }, error.InnerException);
            Assert.DoesNotContain("secret-payload", error.Message);
            Assert.False(smtp.Sent);
        }
        await using var db = f.Factory.CreateDbContext();
        Assert.Null((await db.ProjectInvitations.SingleAsync()).AcceptedAt);
        Assert.Single(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Caller_cancellation_after_commit_does_not_interrupt_delivery()
    {
        await using var f = await InvitationFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        var smtp = new TestSmtpClient("success");
        var sender = new InvitationEmail(Options.Create(SmtpSettings()), new EmailEnvironment(), () =>
        {
            cancellation.Cancel(); // The factory is invoked after the invitation commits.
            return smtp;
        });
        await new InvitationDelivery(f.As("owner"), sender).SendAsync(f.ProjectId, "recipient@example.com", ProjectRole.Viewer, Guid.NewGuid(), cancellation.Token);
        Assert.True(smtp.Sent);
    }

    internal static InvitationEmailOptions SmtpSettings() => new()
    {
        PublicBaseUrl = "https://flags.example.com", From = "invites@example.com", Host = "smtp.example.com",
        Username = "test-user", Password = "test-only-password", Security = SecureSocketOptions.StartTls
    };

    internal sealed class TestSmtpClient(string failure, Action? beforeFailure = null) : SmtpClient
    {
        public bool Sent { get; private set; }
        public bool DidDisconnect { get; private set; }
        public override Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failure == "connect") { beforeFailure?.Invoke(); throw new OperationCanceledException("secret-payload"); }
            return Task.CompletedTask;
        }
        public override Task AuthenticateAsync(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failure == "auth") { beforeFailure?.Invoke(); throw new MailKit.Security.AuthenticationException("secret-payload"); }
            return Task.CompletedTask;
        }
        public override Task<string> SendAsync(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress? progress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failure == "send") { beforeFailure?.Invoke(); throw new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed, "secret-payload"); }
            Sent = true;
            return Task.FromResult("accepted");
        }
        public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        {
            DidDisconnect = true;
            if (failure == "disconnect") throw new IOException("secret-payload");
            return Task.CompletedTask;
        }
    }

    internal static InvitationEmailOptions Settings(string directory) => new()
    {
        PublicBaseUrl = "http://localhost:8080", From = "Feature Flags <invites@example.test>", PickupDirectory = directory
    };
    internal sealed class EmailEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "FeatureFlags";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
