using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FoMed.Api.Controllers;
using FoMed.Api.Middleware;
using FoMed.Infrastructure.DbContext;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class AuthRateLimitAudit
{
    internal static async Task RunIsolatedChecksAsync()
    {
        var checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("Auth limit: " + label); checks++; Console.WriteLine("PASS auth limit: " + label); }
        var defaults = new AuthRateLimitSettings();
        Check(defaults.ResolveSource(false, "Audit") == "Connection", "local uses socket IP");
        Check(defaults.ResolveSource(true, "Audit") == "Service", "Render never shares low client quota by proxy IP");
        Check(defaults.ResolveSource(false, "Production") == "Service", "unknown Production topology uses service fallback");
        foreach (var settings in new[] {
            new AuthRateLimitSettings { ClientIpSource = "TrustedProxy" },
            new AuthRateLimitSettings { ClientIpSource = "Anything" },
            new AuthRateLimitSettings { TrustedProxyIps = ["0.0.0.0"] },
            new AuthRateLimitSettings { TrustedProxyIps = ["::"] },
            new AuthRateLimitSettings { TrustedProxyIps = ["10.0.0.0/8"] },
            new AuthRateLimitSettings { TrustedProxyIps = ["proxy.example"] },
            new AuthRateLimitSettings { Login = new() { PermitLimit = 0 } },
            new AuthRateLimitSettings { Register = new() { WindowSeconds = 86401 } },
            new AuthRateLimitSettings { Login = new() { ServicePermitLimit = 0 } } })
        {
            try { settings.ResolveSource(false, "Audit"); throw new Exception("Unsafe settings accepted"); }
            catch (InvalidOperationException) { Check(true, "unsafe settings rejected before hosting"); }
        }
        try { new AuthRateLimitSettings { ClientIpSource = "Connection" }.ResolveSource(true, "Production"); throw new Exception("Render proxy quota accepted"); }
        catch (InvalidOperationException) { Check(true, "explicit Connection source rejected on Render"); }
        try
        {
            var unsafeForwarding = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true" }).Build();
            new ServiceCollection().AddAuthRateLimits(unsafeForwarding, "Audit");
            throw new Exception("Unrestricted forwarding accepted");
        }
        catch (InvalidOperationException) { Check(true, "unrestricted automatic forwarding rejected"); }
        var ipContext = new DefaultHttpContext(); ipContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.1");
        ipContext.Request.Headers["X-Forwarded-For"] = "203.0.113.4";
        Check(AuthRateLimiting.ClientKey(ipContext, "Connection") == "192.0.2.1", "IPv4-mapped socket normalizes and ignores spoofed IP");
        Check(AuthRateLimiting.ClientKey(ipContext, "TrustedProxy") is null, "forwarded identity needs server-side trusted-peer marker");
        Check(typeof(AuthController).GetMethod("Login")!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName == AuthRateLimiting.LoginPolicy
            && typeof(AuthController).GetMethod("Register")!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName == AuthRateLimiting.RegisterPolicy,
            "actual login/register actions have named policies");
        Check(typeof(AuthController).GetMethod("Refresh")!.GetCustomAttribute<EnableRateLimitingAttribute>() is null
            && typeof(AuthController).GetMethod("ForgotPassword")!.GetCustomAttribute<EnableRateLimitingAttribute>() is null, "refresh/reset flow unchanged in this phase");

        // Small real loopback hosts exercise the production middleware, but use stub handlers,
        // no SQL, no JWT, no config files or external service. Actual auth is tested separately below.
        foreach (var scenario in new[] { "Connection", "TrustedProxy", "UnknownProxy", "RenderAuto", "Disabled", "Concurrent" })
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Audit", ContentRootPath = AppContext.BaseDirectory });
            builder.Configuration.Sources.Clear();
            var values = new Dictionary<string, string?> {
                ["RENDER"] = scenario == "RenderAuto" ? "true" : "false",
                ["AuthRateLimit:ClientIpSource"] = scenario == "UnknownProxy" ? "TrustedProxy" : scenario is "Disabled" or "Concurrent" ? "Connection" : scenario == "RenderAuto" ? "Auto" : scenario,
                ["AuthRateLimit:Enabled"] = scenario == "Disabled" ? "false" : "true",
                ["AuthRateLimit:Login:PermitLimit"] = "2", ["AuthRateLimit:Login:WindowSeconds"] = "1",
                ["AuthRateLimit:Login:ServicePermitLimit"] = "8", ["AuthRateLimit:Login:ServiceWindowSeconds"] = "1",
                ["AuthRateLimit:Register:PermitLimit"] = "2", ["AuthRateLimit:Register:WindowSeconds"] = "1",
                ["AuthRateLimit:Register:ServicePermitLimit"] = "8", ["AuthRateLimit:Register:ServiceWindowSeconds"] = "1"
            };
            if (scenario == "Concurrent")
            {
                values["AuthRateLimit:Login:WindowSeconds"] = "30";
                values["AuthRateLimit:Login:ServiceWindowSeconds"] = "30";
            }
            if (scenario is "TrustedProxy" or "UnknownProxy") values["AuthRateLimit:TrustedProxyIps:0"] = scenario == "TrustedProxy" ? "127.0.0.1" : "192.0.2.10";
            builder.Configuration.AddInMemoryCollection(values);
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            var settings = builder.Services.AddAuthRateLimits(builder.Configuration, "Audit");
            await using var app = builder.Build();
            app.UseRouting(); app.UseAuthProxyIdentity(settings); app.UseRateLimiter();
            var mutations = 0;
            IResult Handler(HttpContext context) { Interlocked.Increment(ref mutations); return Results.Json(new { client = AuthRateLimiting.ClientKey(context, settings.ResolveSource(scenario == "RenderAuto", "Audit")) }); }
            app.MapPost("/login", Handler).WithMetadata(new EnableRateLimitingAttribute(AuthRateLimiting.LoginPolicy));
            app.MapPost("/register", Handler).WithMetadata(new EnableRateLimitingAttribute(AuthRateLimiting.RegisterPolicy));
            app.MapGet("/health", () => Results.Ok());
            await app.StartAsync();
            try
            {
                using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(3) };
                async Task<HttpResponseMessage> Send(string path, string? forwarded = "203.0.113.1")
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, path);
                    if (forwarded is not null) request.Headers.Add("X-Forwarded-For", forwarded);
                    request.Headers.Add("X-User-Id", Guid.NewGuid().ToString());
                    return await client.SendAsync(request);
                }
                if (scenario == "Concurrent")
                {
                    var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Send("/login")));
                    try
                    {
                        Check(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 2 && mutations == 2, "concurrent requests admit exactly the client quota");
                        Check(responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests) == 18, "concurrent excess requests never enter auth handler");
                    }
                    finally { foreach (var response in responses) response.Dispose(); }
                    using var registrationAllowed = await Send("/register");
                    Check(registrationAllowed.StatusCode == HttpStatusCode.OK, "concurrent login exhaustion leaves register independent");
                    using var healthAllowed = await client.GetAsync("/health");
                    Check(healthAllowed.StatusCode == HttpStatusCode.OK, "concurrent login exhaustion leaves health available");
                    continue;
                }
                using var first = await Send("/login"); using var second = await Send("/login");
                Check(first.StatusCode == HttpStatusCode.OK && second.StatusCode == HttpStatusCode.OK, scenario + " admits quota");
                if (scenario is "Connection" or "TrustedProxy")
                {
                    using var denied = await Send("/login");
                    Check(denied.StatusCode == HttpStatusCode.TooManyRequests && mutations == 2, scenario + " rejects immediately before handler");
                    Check(denied.Headers.RetryAfter?.Delta?.TotalSeconds >= 1 && denied.Headers.CacheControl?.NoStore == true, "429 provides bounded wait and no-store");
                    using var spoofed = await Send("/login", "192.0.2.99, 203.0.113.1");
                    Check(spoofed.StatusCode == HttpStatusCode.TooManyRequests, scenario + " spoofed leftmost IP cannot reset quota");
                    using var independent = await Send("/login", "203.0.113.2");
                    Check(independent.StatusCode == (scenario == "TrustedProxy" ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests), scenario + " client isolation follows verified identity only");
                    using var otherPolicy = await Send("/register");
                    Check(otherPolicy.StatusCode == HttpStatusCode.OK, "register has a separate quota");
                    await Task.Delay(1250);
                    using var replenished = await Send("/login"); Check(replenished.StatusCode == HttpStatusCode.OK, "quota recovers after window without restart");
                    using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
                    Check(body.RootElement.GetProperty("client").GetString() == (scenario == "Connection" ? "127.0.0.1" : "203.0.113.1"), scenario + " verified client key");
                    if (scenario == "TrustedProxy")
                    {
                        foreach (var missingOrInvalid in new string?[] { null, "not-an-ip" })
                        {
                            using var fallback = await Send("/login", missingOrInvalid);
                            using var fallbackBody = JsonDocument.Parse(await fallback.Content.ReadAsStringAsync());
                            Check(fallback.StatusCode == HttpStatusCode.OK && fallbackBody.RootElement.GetProperty("client").ValueKind == JsonValueKind.Null,
                                "trusted peer with absent/invalid forwarding uses service fallback, not proxy-IP quota");
                        }
                    }
                }
                else if (scenario == "Disabled")
                {
                    for (var i = 0; i < 9; i++) { using var allowed = await Send("/login"); Check(allowed.StatusCode == HttpStatusCode.OK, "explicit disable is honored"); }
                }
                else
                {
                    using var body = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
                    Check(body.RootElement.GetProperty("client").ValueKind == JsonValueKind.Null, scenario + " ignores unverified header/socket as client identity");
                    for (var i = 0; i < 6; i++) { using var allowed = await Send("/login", $"203.0.113.{i + 2}"); Check(allowed.StatusCode == HttpStatusCode.OK, "shared guard is not the low proxy-IP quota"); }
                    using var denied = await Send("/login", "203.0.113.80"); Check(denied.StatusCode == HttpStatusCode.TooManyRequests, "changing spoofed headers cannot bypass service guard");
                }
                for (var i = 0; i < 5; i++) { using var health = await client.GetAsync("/health"); Check(health.StatusCode == HttpStatusCode.OK, "health unaffected by exhausted login quota"); }
            }
            finally { await app.StopAsync(); }
        }
        Console.WriteLine($"PASS: {checks} auth rate-limit configuration/proxy/loopback checks. No database or external requests.");
    }

    internal static async Task RunHttpAsync(DbContextOptions<FoMedDbContext> options, HttpClient client, Action<bool, string, object?> check)
    {
        async Task<HttpResponseMessage> Login(string password, bool spoof = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { username = "patient", password }) };
            request.Headers.Add("Origin", "http://localhost:5174");
            if (spoof) request.Headers.Add("X-Forwarded-For", "203.0.113.123");
            return await client.SendAsync(request);
        }
        using var ok = await Login("Audit-Only!2026"); check(ok.StatusCode == HttpStatusCode.OK, "Auth limit: valid login still succeeds", (int)ok.StatusCode);
        using var wrong = await Login("Wrong-Audit-Only!"); check(wrong.StatusCode == HttpStatusCode.Unauthorized, "Auth limit: invalid credentials retain 401 within quota", (int)wrong.StatusCode);
        using var denied = await Login("Audit-Only!2026", true); check(denied.StatusCode == HttpStatusCode.TooManyRequests, "Auth limit: spoofed IP cannot avoid exhausted real login quota", (int)denied.StatusCode);
        using var body = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
        check(body.RootElement.GetProperty("statusCode").GetInt32() == 429 && body.RootElement.GetProperty("retryAfterSeconds").GetInt32() > 0
            && body.RootElement.GetProperty("message").GetString()!.Contains("Vui lòng chờ"), "Auth limit: actual 429 is accented and has safe retry metadata", null);
        check(denied.Headers.RetryAfter?.Delta?.TotalSeconds >= 1 && denied.Headers.GetValues("Access-Control-Expose-Headers").Any(h => h.Contains("Retry-After", StringComparison.OrdinalIgnoreCase)),
            "Auth limit: allowed frontend can read Retry-After across origins", null);
        var registration = new { fullName = "Bệnh nhân kiểm thử giới hạn", phone = "0987654321", password = "Audit-Only!2026" };
        using var created = await client.PostAsJsonAsync("/api/auth/register", registration); check(created.StatusCode == HttpStatusCode.Created, "Auth limit: register independent from login quota", (int)created.StatusCode);
        using var duplicate = await client.PostAsJsonAsync("/api/auth/register", registration); check(duplicate.StatusCode == HttpStatusCode.Conflict, "Auth limit: duplicate account retains existing conflict", (int)duplicate.StatusCode);
        using var blocked = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Không được tạo", phone = "0987654322", password = "Audit-Only!2026" });
        check(blocked.StatusCode == HttpStatusCode.TooManyRequests, "Auth limit: over-quota registration blocked", (int)blocked.StatusCode);
        await using (var db = new FoMedDbContext(options))
            check(!await db.Patients.AnyAsync(p => p.Phone == "0987654322") && await db.Patients.CountAsync(p => p.Phone == "0987654321") == 1, "Auth limit: blocked registration creates no user/patient", null);
        using var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "fixture-invalid-not-real" });
        check(refresh.StatusCode == HttpStatusCode.Unauthorized, "Auth limit: exhausted login does not block refresh policy", (int)refresh.StatusCode);
        using var health = await client.GetAsync("/health"); check(health.StatusCode == HttpStatusCode.OK, "Auth limit: actual health remains available", (int)health.StatusCode);
        await Task.Delay(4250);
        using var resumed = await Login("Audit-Only!2026"); check(resumed.StatusCode == HttpStatusCode.OK, "Auth limit: real login recovers after wait", (int)resumed.StatusCode);
    }
}
