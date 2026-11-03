using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using PoepleDirectoryApplication.Api.Models;
using PoepleDirectoryApplication.Api.Providers;
using PoepleDirectoryApplication.Api.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOptions<MailSettings>().BindConfiguration("MailSettings").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<EmailApiOptions>().BindConfiguration("EmailApi")
    .Validate(x => !string.IsNullOrWhiteSpace(x.ApiKey) && x.ApiKey.Length >= 32, "Configure EmailApi:ApiKey with at least 32 characters.").ValidateOnStart();
builder.Services.AddScoped<IEmailProvider, EmailProvider>();
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("email", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.Run();
public partial class Program { }
