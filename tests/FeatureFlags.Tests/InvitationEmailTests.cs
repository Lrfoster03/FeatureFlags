using FeatureFlags.Components.Models;
using FeatureFlags.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using MimeKit;

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
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(issued));
        settings.PublicBaseUrl = "https://flags.example.com";
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(issued));
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
