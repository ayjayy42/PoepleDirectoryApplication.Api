using System.ComponentModel.DataAnnotations;
namespace PoepleDirectoryApplication.Api.Models;
public sealed class MailSettings
{
    [Required] public string SmtpServer { get; set; } = string.Empty;
    [Range(1, 65535)] public int SmtpPort { get; set; } = 587;
    [Required, EmailAddress] public string ToAddress { get; set; } = string.Empty;
    [Required, EmailAddress] public string FromAddress { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; } = true;
    [Range(1, 120)] public int SendTimeoutSeconds { get; set; } = 30;
}
public sealed class EmailApiOptions
{
    public string ApiKey { get; set; } = string.Empty;
}
