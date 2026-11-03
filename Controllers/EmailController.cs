using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PoepleDirectoryApplication.Api.Models;
using PoepleDirectoryApplication.Api.Providers;
namespace PoepleDirectoryApplication.Api.Controllers;

[ApiController, Authorize, EnableRateLimiting("email"), Route("api/email")]
public sealed class EmailController(IEmailProvider emailProvider, ILogger<EmailController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SendEmail(EmailRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await emailProvider.SendMailAsync(new MailData { EmailSubject = request.Subject.Trim(), EmailBody = request.Body }, cancellationToken);
            return Ok(new { message = "Email sent successfully." });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("SMTP delivery timed out.");
            return Problem(statusCode: 503, title: "Email delivery timed out.");
        }
        catch (SmtpException ex)
        {
            logger.LogError(ex, "SMTP delivery failed.");
            return Problem(statusCode: 502, title: "Email delivery failed.");
        }
    }
}
