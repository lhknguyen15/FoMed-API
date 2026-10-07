using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace FoMed.Api.Middleware;

public sealed class AuthRequestLimit
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
    public int ServicePermitLimit { get; set; } = 200;
    public int ServiceWindowSeconds { get; set; } = 60;
    public bool IsValid() => PermitLimit is >= 1 and <= 10000 && ServicePermitLimit is >= 1 and <= 10000
        && WindowSeconds is >= 1 and <= 86400 && ServiceWindowSeconds is >= 1 and <= 86400;
}

public sealed class AuthRateLimitSettings
{
    public bool Enabled { get; set; } = true;
    // Auto trusts the socket IP only in local Development/Audit, never Render's proxy IP.
    public string ClientIpSource { get; set; } = "Auto";
    public string[] TrustedProxyIps { get; set; } = [];
    public AuthRequestLimit Login { get; set; } = new();
    public AuthRequestLimit Register { get; set; } = new() { PermitLimit = 5, WindowSeconds = 600, ServicePermitLimit = 50 };

    public string ResolveSource(bool render, string environment)
    {
        if (ClientIpSource is not ("Auto" or "Connection" or "TrustedProxy" or "Service")
            || Login is null || Register is null || !Login.IsValid() || !Register.IsValid()
            || TrustedProxyIps is null || TrustedProxyIps.Length > 32
            || TrustedProxyIps.Any(value => !IPAddress.TryParse(value, out var ip)
                || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast)))
            throw new InvalidOperationException("AuthRateLimit requires bounded positive limits and exact trusted proxy IPs.");
        var source = ClientIpSource == "Auto"
            ? TrustedProxyIps.Length > 0 ? "TrustedProxy" : !render && (environment is "Development" or "Audit") ? "Connection" : "Service"
            : ClientIpSource;
        if (source == "Connection" && render)
            throw new InvalidOperationException("Render must not apply a client quota to a shared proxy connection IP.");
        if (source == "TrustedProxy" && TrustedProxyIps.Length == 0)
            throw new InvalidOperationException("TrustedProxy requires explicitly verified ingress proxy IPs.");
        return source;
    }
}

public static class AuthRateLimiting
{
    public const string LoginPolicy = "auth-login";
    public const string RegisterPolicy = "auth-register";
    private static readonly object ProxyPeerKey = new();
    private sealed record ProxyPeer(IPAddress? Address, bool Trusted);

    public static AuthRateLimitSettings AddAuthRateLimits(this IServiceCollection services, IConfiguration configuration, string environment)
    {
        var settings = configuration.GetSection("AuthRateLimit").Get<AuthRateLimitSettings>() ?? new();
        if (configuration.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED"))
            throw new InvalidOperationException("Do not enable unrestricted automatic forwarded headers; configure verified AuthRateLimit proxy IPs instead.");
        var source = settings.ResolveSource(CloudHostingConfiguration.IsRender(configuration), environment);
        services.AddSingleton(settings);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (rejection, ct) =>
            {
                var seconds = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
                    ? Math.Clamp((int)Math.Ceiling(wait.TotalSeconds), 1, 86400) : 60;
                var response = rejection.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;
                response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                response.Headers.CacheControl = "no-store";
                await response.WriteAsJsonAsync(new
                {
                    dataResponse = (object?)null, statusCode = 429, retryAfterSeconds = seconds,
                    message = $"Bạn thao tác quá nhanh. Vui lòng chờ {seconds} giây rồi thử lại."
                }, ct);
            };
            // The service guard runs first, and ONLY on the two annotated auth endpoints.
            // It also provides a bounded fallback when a cloud client IP is not verified.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var policy = Policy(context);
                var limit = Limit(settings, policy);
                return !settings.Enabled || limit is null ? RateLimitPartition.GetNoLimiter("unlimited")
                    : Window("service:" + policy, limit.ServicePermitLimit, limit.ServiceWindowSeconds);
            });
            foreach (var policy in new[] { LoginPolicy, RegisterPolicy })
                options.AddPolicy(policy, context =>
                {
                    var client = ClientKey(context, source);
                    var limit = Limit(settings, policy)!;
                    return !settings.Enabled || client is null ? RateLimitPartition.GetNoLimiter("unverified-client")
                        : Window(policy + ":" + client, limit.PermitLimit, limit.WindowSeconds);
                });
        });
        return settings;
    }

    private static RateLimitPartition<string> Window(string key, int permits, int seconds) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits, Window = TimeSpan.FromSeconds(seconds), AutoReplenishment = true, QueueLimit = 0
        });
    private static string? Policy(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
    private static AuthRequestLimit? Limit(AuthRateLimitSettings settings, string? policy) =>
        policy == LoginPolicy ? settings.Login : policy == RegisterPolicy ? settings.Register : null;
    private static IPAddress? Normalize(IPAddress? ip) => ip?.IsIPv4MappedToIPv6 == true ? ip.MapToIPv4() : ip;

    public static string? ClientKey(HttpContext context, string source)
    {
        var address = Normalize(context.Connection.RemoteIpAddress);
        if (source == "Connection") return address?.ToString(); // Ignore all caller-provided identity headers.
        if (source != "TrustedProxy" || context.Items[ProxyPeerKey] is not ProxyPeer { Trusted: true } peer
            || address is null || address.Equals(peer.Address)) return null;
        return address.ToString(); // Set by ASP.NET's strict, one-hop forwarded-headers middleware.
    }

    public static void UseAuthProxyIdentity(this WebApplication app, AuthRateLimitSettings settings)
    {
        var source = settings.ResolveSource(CloudHostingConfiguration.IsRender(app.Configuration), app.Environment.EnvironmentName);
        if (!settings.Enabled) return;
        if (source == "Service")
        {
            app.Logger.LogWarning("Auth client-IP quotas are inactive: ingress proxy identity is not verified. Login/register service quotas remain active.");
            return;
        }
        if (source != "TrustedProxy") return;
        var trusted = settings.TrustedProxyIps.Select(value => Normalize(IPAddress.Parse(value))!).ToHashSet();
        var forwarding = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor, ForwardLimit = 1 };
        forwarding.KnownProxies.Clear();
        forwarding.KnownIPNetworks.Clear();
        foreach (var ip in trusted)
        {
            forwarding.KnownProxies.Add(ip);
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) forwarding.KnownProxies.Add(ip.MapToIPv6());
        }
        // Limit forwarding to auth; do not change clinical audit IPs, host, scheme or routing.
        app.UseWhen(context => Limit(settings, Policy(context)) is not null, branch =>
        {
            branch.Use(async (context, next) =>
            {
                var original = Normalize(context.Connection.RemoteIpAddress);
                context.Items[ProxyPeerKey] = new ProxyPeer(original, original is not null && trusted.Contains(original));
                await next(context);
            });
            branch.UseForwardedHeaders(forwarding);
        });
    }
}
