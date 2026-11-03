using System.ComponentModel.DataAnnotations;
namespace PoepleDirectoryApplication.Api.Models;
public sealed class EmailRequest
{
    [Required, StringLength(200), RegularExpression(@"^[^\r\n]+$")]
    public string Subject { get; set; } = string.Empty;
    [Required, StringLength(10000)]
    public string Body { get; set; } = string.Empty;
}
