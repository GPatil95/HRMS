using Hrms.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Scriban;
using Scriban.Runtime;

namespace Hrms.Infrastructure.Messaging;

public sealed class SmtpOptions
{
    public const string Section = "Email:Smtp";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseStartTls { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@hrms.local";
    public string FromName { get; set; } = "HRMS";
}

public sealed class SmtpEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    public async Task<string?> SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        var o = options.Value;
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        msg.To.AddRange(email.To.Select(MailboxAddress.Parse));
        msg.Cc.AddRange(email.Cc.Select(MailboxAddress.Parse));
        msg.Subject = email.Subject;
        var body = new BodyBuilder { HtmlBody = email.HtmlBody, TextBody = email.TextBody };
        foreach (var a in email.Attachments)
        {
            body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        }

        msg.Body = body.ToMessageBody();
        using var client = new SmtpClient();
        await client.ConnectAsync(o.Host, o.Port, o.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
        if (!string.IsNullOrEmpty(o.Username))
        {
            await client.AuthenticateAsync(o.Username, o.Password ?? string.Empty, ct);
        }

        var response = await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
        return msg.MessageId ?? response;
    }
}

/// <summary>Scriban renderer in a sandboxed context: no imports, HTML-escaped output, bounded loops/recursion.</summary>
public sealed class ScribanTemplateRenderer : ITemplateRenderer
{
    public string Render(string template, IReadOnlyDictionary<string, object?> model)
    {
        var parsed = Template.Parse(template);
        if (parsed.HasErrors)
        {
            throw new InvalidOperationException("Template has syntax errors.");
        }

        var globals = new ScriptObject();
        foreach (var (k, v) in model)
        {
            globals[k] = v is string s ? System.Net.WebUtility.HtmlEncode(s) : v;
        }

        var ctx = new TemplateContext { LoopLimit = 1000, RecursiveLimit = 20, EnableRelaxedMemberAccess = true, MemberRenamer = m => m.Name };
        ctx.PushGlobal(globals);
        return parsed.Render(ctx);
    }
}

public sealed class NoopPushNotificationSender : IPushNotificationSender
{
    public Task SendAsync(Guid tenantId, Guid userId, string title, string? body, CancellationToken ct) => Task.CompletedTask;
}
