using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PoepleDirectoryApplication.Api.Models;
namespace PoepleDirectoryApplication.Api.Security;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IOptions<EmailApiOptions> apiOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var supplied) || supplied.Count != 1)
            return Task.FromResult(AuthenticateResult.NoResult());
        var expected = apiOptions.Value.ApiKey;
        var valid = !string.IsNullOrWhiteSpace(expected) && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString())),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
        if (!valid) return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "email-client") }, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
