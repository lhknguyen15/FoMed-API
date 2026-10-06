using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FoMed.Api.Middleware;
using FoMed.Application.Services.Clinical;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

internal static class CloudHostingAudit
{
    private sealed class DemoEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HostingAudit";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    public static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("Hosting audit: " + label); checks++; Console.WriteLine("PASS hosting: " + label); }
        IConfiguration Config(params (string Key, string Value)[] values) => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();
        const string fakeConnection = "Server=tcp:fake-demo.database.windows.net,1433;Database=FoMedDbDemo;User ID=fake;Password=Fake-Audit-Only;Encrypt=True;TrustServerCertificate=False;Connection Timeout=5";
        CloudHostingConfiguration.ValidateRenderDatabase(Config(("RENDER", "true"), ("ConnectionStrings:DefaultConnection", fakeConnection)));
        Check(true, "Azure SQL over TLS accepted without opening connection");
        CloudHostingConfiguration.ValidateRenderDatabase(Config()); Check(true, "local startup remains independent of Render requirements");
        foreach (var invalid in new[] { "", "Server=localhost;Database=FoMedDb;Integrated Security=true", "bad-string",
            fakeConnection.Replace("Encrypt=True", "Encrypt=False"), fakeConnection.Replace("TrustServerCertificate=False", "TrustServerCertificate=True"),
            fakeConnection.Replace("Database=FoMedDbDemo", "Database=master"), fakeConnection.Replace("Password=Fake-Audit-Only", "Password="),
            fakeConnection.Replace(",1433", ",9999"), fakeConnection.Replace("fake-demo.database.windows.net", "fake-demo.database.windows.net.evil.example") })
        {
            try { CloudHostingConfiguration.ValidateRenderDatabase(Config(("RENDER", "true"), ("ConnectionStrings:DefaultConnection", invalid))); throw new Exception("unsafe database accepted"); }
            catch (InvalidOperationException e) { Check(!e.Message.Contains("Fake-Audit-Only"), "unsafe cloud DB rejected without credential leakage"); }
        }
        Check(CloudHostingConfiguration.CorsOrigins(Config(), true).Length == 0, "no implicit wildcard CORS");
        foreach (var origin in new[] { "https://fomed.example", "http://localhost:5174", "http://127.0.0.1:5174" })
            Check(CloudHostingConfiguration.CorsOrigins(Config(("Cors:AllowedOrigins:0", origin)), true).Single() == origin, "exact allowed origin accepted");
        foreach (var origin in new[] { "*", "https://fomed.example/", "https://fomed.example/path", "https://name:secret@fomed.example", "https://fomed.example?x=y", "http://public.example" })
        {
            try { CloudHostingConfiguration.CorsOrigins(Config(("Cors:AllowedOrigins:0", origin)), true); throw new Exception("unsafe origin accepted"); }
            catch (InvalidOperationException) { Check(true, "unsafe CORS origin rejected"); }
        }
        var storage = new PrivateClinicalAttachmentStore(new DemoEnvironment(), Config(("ClinicalAttachments:Enabled", "false")));
        try { await storage.SaveAsync([1], ".pdf", default); throw new Exception("ephemeral upload accepted"); }
        catch (ClinicException e) { Check(e.StatusCode == 503, "disabled cloud storage rejects upload before any file write"); }
        try { await storage.ReadAsync("invalid", default); throw new Exception("disabled download accepted"); }
        catch (ClinicException e) { Check(e.StatusCode == 503, "disabled cloud download explains storage limitation"); }

        // Run published Production assembly on loopback, with fake unreachable SQL and no private configs.
        // /health and CORS/401 must work without ever opening SQL. This is not a Docker/cloud test.
        var output = Path.GetFullPath("FoMed-API/FoMed.Api/bin/CloudPublishAudit");
        if (!File.Exists(Path.Combine(output, "FoMed.Api.dll"))) throw new Exception("Publish FoMed.Api to bin/CloudPublishAudit first.");
        Check(!File.Exists(Path.Combine(output, "appsettings.Development.json")), "publish excludes private Development configuration");
        var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = output, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(output, "FoMed.Api.dll")); start.ArgumentList.Add("--urls=http://127.0.0.1:" + port);
        foreach (var item in new Dictionary<string, string> {
            ["ASPNETCORE_ENVIRONMENT"] = "Production", ["RENDER"] = "true", ["ConnectionStrings__DefaultConnection"] = fakeConnection,
            ["Jwt__Key"] = "Fake-Hosting-Audit-Only-Not-Real-Key-2026", ["Jwt__Issuer"] = "FakeAudit", ["Jwt__Audience"] = "FakeAudit",
            ["Cors__AllowedOrigins__0"] = "http://localhost:5174", ["ClinicalAttachments__Enabled"] = "false", ["SePay__Enabled"] = "false",
            ["Logging__LogLevel__Default"] = "None", ["Logging__Console__LogLevel__Default"] = "None" }) start.Environment[item.Key] = item.Value;
        using var api = Process.Start(start) ?? throw new Exception("Cannot start hosting audit API");
        api.BeginOutputReadLine(); api.BeginErrorReadLine();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new("http://127.0.0.1:" + port), Timeout = TimeSpan.FromSeconds(2) };
            var ready = false;
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (api.HasExited) throw new Exception("Production API exited during startup");
                try { using var response = await client.GetAsync("/health"); ready = response.StatusCode == HttpStatusCode.OK; if (ready) break; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(100);
            }
            Check(ready, "Production health responds without SQL, redirect or Development secrets");
            using var health = await client.GetAsync("/health"); Check(await health.Content.ReadAsStringAsync() == "{\"status\":\"ok\"}", "health leaks no DB/secret details");
            using var swagger = await client.GetAsync("/swagger/v1/swagger.json"); Check(swagger.StatusCode == HttpStatusCode.NotFound, "Swagger not published in Production");
            using var protectedRoute = await client.GetAsync("/api/invoices"); Check(protectedRoute.StatusCode == HttpStatusCode.Unauthorized, "protected route still requires JWT");
            async Task<HttpResponseMessage> Preflight(string origin)
            {
                using var request = new HttpRequestMessage(HttpMethod.Options, "/api/invoices"); request.Headers.Add("Origin", origin);
                request.Headers.Add("Access-Control-Request-Method", "POST"); request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
                return await client.SendAsync(request);
            }
            using var allowed = await Preflight("http://localhost:5174");
            Check(allowed.StatusCode == HttpStatusCode.NoContent && allowed.Headers.GetValues("Access-Control-Allow-Origin").Single() == "http://localhost:5174", "allowed frontend preflight succeeds before auth");
            Check(!allowed.Headers.Contains("Access-Control-Allow-Credentials"), "CORS does not enable cookies/credentials or wildcard");
            using var denied = await Preflight("https://evil.example"); Check(!denied.Headers.Contains("Access-Control-Allow-Origin"), "unlisted origin denied");
        }
        finally { if (!api.HasExited) { api.Kill(true); await api.WaitForExitAsync(); } }
        Console.WriteLine($"PASS: {checks} deployment configuration/Production HTTP checks; no cloud resource or database changes.");
    }
}
