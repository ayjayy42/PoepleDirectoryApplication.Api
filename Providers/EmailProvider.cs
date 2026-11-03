using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using PoepleDirectoryApplication.Api.Models;
namespace PoepleDirectoryApplication.Api.Providers;

public interface IEmailProvider
{
    Task SendMailAsync(MailData mailData, CancellationToken cancellationToken = default);
}

public sealed class EmailProvider(IOptions<MailSettings> options) : IEmailProvider
{
    public async Task SendMailAsync(MailData mailData, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        using var message = new MailMessage(settings.FromAddress, settings.ToAddress)
        {
            Subject = mailData.EmailSubject, Body = mailData.EmailBody,
        };
        using var smtp = new SmtpClient(settings.SmtpServer, settings.SmtpPort)
        {
            UseDefaultCredentials = false, EnableSsl = settings.EnableSsl,
        };
        if (!string.IsNullOrWhiteSpace(settings.UserName))
            smtp.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.SendTimeoutSeconds));
        await smtp.SendMailAsync(message, deadline.Token);
    }
}
