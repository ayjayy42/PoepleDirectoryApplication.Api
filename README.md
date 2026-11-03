# Email API

Requires .NET 10. Existing SMTP settings remain in `appsettings.json`; configure valid sender/recipient/SMTP details before delivery. The endpoint requires a configured API key in every environment and allows 30 calls per minute per client IP.

```powershell
$env:EmailApi__ApiKey = '<a random value of at least 32 characters>'
dotnet run
```

Send `POST /api/email` with `X-Api-Key` and JSON `{ "subject": "Hello", "body": "Message" }`. Responses: 200 delivered, 400 invalid input, 401 missing/incorrect key, 429 rate limit, 502 SMTP failure, 503 delivery timeout. Request cancellation stops pending work. The recipient is configuration-controlled, not chosen by callers. Use HTTPS outside loopback development.

`dotnet test -c Release` executes API integration tests with a fake delivery provider; it never sends email. The API registers the real provider and validates SMTP/API-key options at startup. No SMTP credentials were changed.
