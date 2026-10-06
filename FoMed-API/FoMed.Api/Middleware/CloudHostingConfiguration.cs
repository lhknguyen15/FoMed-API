using Microsoft.Data.SqlClient;

namespace FoMed.Api.Middleware;

public static class CloudHostingConfiguration
{
    public static bool IsRender(IConfiguration configuration) =>
        string.Equals(configuration["RENDER"], "true", StringComparison.OrdinalIgnoreCase);

    public static void ValidateRenderDatabase(IConfiguration configuration)
    {
        if (!IsRender(configuration)) return;
        const string error = "Render demo requires an Azure SQL application database over verified TLS with SQL credentials. Configure ConnectionStrings__DefaultConnection in Render; never use local SQL or copy credentials into source.";
        SqlConnectionStringBuilder sql;
        try { sql = new(configuration.GetConnectionString("DefaultConnection")); }
        catch (ArgumentException) { throw new InvalidOperationException(error); }
        var host = sql.DataSource.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? sql.DataSource[4..] : sql.DataSource;
        var parts = host.Split(',');
        if (parts.Length > 2 || (parts.Length == 2 && parts[1] != "1433")
            || !System.Text.RegularExpressions.Regex.IsMatch(parts[0], "^[a-zA-Z0-9-]+\\.database\\.windows\\.net$")
            || string.IsNullOrWhiteSpace(sql.InitialCatalog)
            || new[] { "master", "model", "msdb", "tempdb" }.Contains(sql.InitialCatalog, StringComparer.OrdinalIgnoreCase)
            || sql.TrustServerCertificate || sql.Encrypt == SqlConnectionEncryptOption.Optional || sql.IntegratedSecurity
            || string.IsNullOrWhiteSpace(sql.UserID) || string.IsNullOrWhiteSpace(sql.Password))
            throw new InvalidOperationException(error);
    }

    public static string[] CorsOrigins(IConfiguration configuration, bool production)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        foreach (var value in origins)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0
                || value != uri.GetLeftPart(UriPartial.Authority)
                || (production && uri.Scheme == "http" && !uri.IsLoopback))
                throw new InvalidOperationException("Cors:AllowedOrigins requires exact HTTP(S) origins without path, trailing slash or wildcard; Production requires HTTPS except loopback demo clients.");
        }
        return origins.Distinct(StringComparer.Ordinal).ToArray();
    }
}
