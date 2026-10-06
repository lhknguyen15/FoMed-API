using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;

// Standalone, never referenced by the API or its startup. No secrets in arguments/logs.
const string TargetServer = "fomed-sql-demo-nguyen.database.windows.net";
const string SourceDatabase = "FoMedDb";
const string TargetDatabase = "FoMedDbDemo";
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
// This runner is built into tools/DatabaseTransfer/bin/Debug/net10.0 (or Release).
var privateDir = Path.Combine(repo, "deploy-private");
var packagePath = Path.Combine(privateDir, "FoMedDb-demo.bacpac");
var manifestPath = Path.Combine(privateDir, "FoMedDb-demo.manifest.local.json");

try
{
    if (args is ["--self-test"])
    {
        TransferTests.Run();
        return;
    }
    if (args.Length != 1 || args[0] is not ("inspect" or "export" or "import" or "verify" or "diagnose"))
    {
        Console.WriteLine("Usage: dotnet run --project tools/DatabaseTransfer -- inspect|export|import|verify|diagnose|--self-test");
        Console.WriteLine("Export requires paused writes; import prompts privately and only targets the existing empty Azure demo DB.");
        return;
    }
    if (!File.Exists(Path.Combine(repo, "FoMed-API/FoMed.Api/FoMed.Api.csproj")))
        throw new TransferFailure("Run the tool from its standard project build directory; repository not found.");

    var action = args[0];
    if (action is "inspect" or "export")
    {
        // Do not echo this file or its connection string: it belongs to the user.
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo,
            "FoMed-API/FoMed.Api/appsettings.Development.json")));
        var source = new SqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings")
            .GetProperty("DefaultConnection").GetString());
        TransferGuards.Source(source);
        source.ApplicationName = "FoMed.DatabaseTransfer";
        source.ConnectTimeout = 30;
        await using var sql = new SqlConnection(source.ConnectionString);
        await sql.OpenAsync();
        if (await Scalar(sql, "SELECT CONVERT(int, SERVERPROPERTY('EngineEdition'))") == 5)
            throw new TransferFailure("Source must be local SQL Server, not Azure SQL.");
        var before = await Capture(sql);
        Show("Local", before);
        if (action == "inspect") return;

        if (Process.GetProcessesByName("FoMed.Api").Length != 0)
            throw new TransferFailure("Stop the local FoMed.Api before export. This tool never stops it for you.");
        Console.WriteLine("Pause ALL other applications/tests that can write to FoMedDb. Local data is not modified.");
        Confirm("Type WRITES-PAUSED to confirm no writes during export:", "WRITES-PAUSED");
        if (File.Exists(packagePath) || File.Exists(manifestPath))
            throw new TransferFailure("An export already exists. Preserve it; this tool refuses to overwrite backups.");
        Directory.CreateDirectory(privateDir);
        Console.WriteLine("Exporting local demo schema and data. No Azure connection is made.");
        var dac = new DacServices(source.ConnectionString);
        dac.ExportBacpac(packagePath, SourceDatabase, new DacExportOptions
        {
            VerifyExtraction = true,
            CommandTimeout = 120
        }, tables: null);
        var after = await Capture(sql);
        TransferGuards.SameSnapshot(before, after);
        var manifest = new ExportManifest(SourceDatabase, TargetServer, TargetDatabase,
            DateTimeOffset.UtcNow, await Hash(packagePath), before);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, jsonOptions));
        Console.WriteLine("PASS: Export verified; package + private row-count/schema manifest saved in deploy-private.");
        Console.WriteLine("BACPAC contains account data. Never commit/share it. Clinical files are NOT included.");
        return;
    }

    if (!File.Exists(packagePath) || !File.Exists(manifestPath))
        throw new TransferFailure("Export package and manifest must exist before connecting to Azure.");
    var saved = JsonSerializer.Deserialize<ExportManifest>(await File.ReadAllTextAsync(manifestPath))
        ?? throw new TransferFailure("Invalid export manifest.");
    if (saved.Source != SourceDatabase || saved.Server != TargetServer || saved.Database != TargetDatabase
        || saved.Sha256 != await Hash(packagePath))
        throw new TransferFailure("Export checksum/target mismatch. Import is blocked.");
    Console.WriteLine($"Target: {TargetServer} / {TargetDatabase}");
    if (Console.IsInputRedirected)
        throw new TransferFailure("Azure credentials must be entered directly in an interactive terminal, never through tool/chat input.");
    Console.Write("SQL admin login (default fomedadmin): ");
    var username = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(username)) username = "fomedadmin";
    var cloud = new SqlConnectionStringBuilder
    {
        DataSource = $"tcp:{TargetServer},1433", InitialCatalog = TargetDatabase,
        UserID = username, Password = ReadPassword(), Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = false, PersistSecurityInfo = false, ConnectTimeout = 120,
        ApplicationName = "FoMed.DatabaseTransfer"
    };
    await using var azure = new SqlConnection(cloud.ConnectionString);
    Console.WriteLine("Connecting to Azure SQL; waiting for server response (timeout 120 seconds)...");
    await azure.OpenAsync();
    if (await Scalar(azure, "SELECT CONVERT(int, SERVERPROPERTY('EngineEdition'))") != 5)
        throw new TransferFailure("Target is not Azure SQL Database. No import performed.");
    await using (var check = new SqlCommand("SELECT DB_NAME()", azure))
        if ((string?)await check.ExecuteScalarAsync() != TargetDatabase)
            throw new TransferFailure("Wrong target database. No import performed.");
    var tierBefore = await Tier(azure);
    Console.WriteLine($"Existing Azure tier: {tierBefore}");
    if (action == "import")
    {
        var objects = await Scalar(azure, "SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0")
            + await Scalar(azure, "SELECT COUNT(*) FROM sys.schemas WHERE schema_id < 16384 AND name NOT IN ('dbo','guest','INFORMATION_SCHEMA','sys')")
            + await Scalar(azure, "SELECT COUNT(*) FROM sys.types WHERE is_user_defined = 1");
        TransferGuards.EmptyTarget(objects);
        Console.WriteLine("Confirm Azure still shows Free Offer and Overage billing Disabled. This tool never creates another DB or sets a pricing tier.");
        Confirm("Type IMPORT-FoMedDbDemo to authorize importing this demo copy:", "IMPORT-FoMedDbDemo");
        using var package = BacPackage.Load(packagePath);
        var dac = new DacServices(cloud.ConnectionString);
        // Existing DB only: no SKU, max size, service objective or Create Database parameters.
        dac.ImportBacpac(package, TargetDatabase, new DacImportOptions { CommandTimeout = 120 });
    }
    if (tierBefore != await Tier(azure))
        throw new TransferFailure("Azure service objective changed. Check Free Offer/Overage in portal before any further action.");
    Console.WriteLine("Reading Azure table counts and schema inventory (read-only)...");
    var actual = await Capture(azure);
    Show("Azure", actual);
    Show("Export", saved.Snapshot);
    var baseline = saved.Snapshot;
    if (baseline.InventoryHash != actual.InventoryHash
        && (baseline.InventoryEntries is null || baseline.SystemNamedIdentities is null))
    {
        Console.WriteLine("Reading local metadata to enrich the legacy export baseline (read-only)...");
        using var localConfig = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo,
            "FoMed-API/FoMed.Api/appsettings.Development.json")));
        var localBuilder = new SqlConnectionStringBuilder(localConfig.RootElement.GetProperty("ConnectionStrings")
            .GetProperty("DefaultConnection").GetString());
        TransferGuards.Source(localBuilder);
        localBuilder.ApplicationName = "FoMed.DatabaseTransfer.Verify";
        localBuilder.ConnectTimeout = 30;
        await using var localSql = new SqlConnection(localBuilder.ConnectionString);
        await localSql.OpenAsync();
        var localNow = await Capture(localSql);
        if (localNow.InventoryHash == baseline.InventoryHash)
        {
            // Keep ORIGINAL exported row counts/hash; only add metadata proven to
            // match that hash. Never rewrite the backup or the legacy manifest.
            baseline = baseline with
            {
                InventoryEntries = localNow.InventoryEntries,
                SystemNamedIdentities = localNow.SystemNamedIdentities
            };
        }
        else Console.WriteLine("Local schema changed since export; cannot normalize this legacy baseline. Verification remains strict.");
    }
    var differences = TransferGuards.Differences(baseline, actual);
    if (baseline.InventoryHash != actual.InventoryHash && differences.Count == 0)
        Console.WriteLine("INFO: Structure matches; only SQL system-generated constraint names differ. Explicit names and all captured structural properties remain checked.");
    foreach (var difference in differences.Take(40)) Console.WriteLine("DIFF: " + difference);
    if (action == "diagnose")
    {
        Console.WriteLine("DIAGNOSE: SELECT queries only; no import, DELETE, schema changes or reset.");
        await ShowObjects(azure);
        if (saved.Snapshot.InventoryHash != actual.InventoryHash)
        {
            if (baseline.InventoryEntries is not null)
            {
                var sourceEntries = baseline.InventoryEntries;
                var targetEntries = actual.InventoryEntries!;
                var missing = sourceEntries.Except(targetEntries, StringComparer.Ordinal).ToArray();
                var extra = targetEntries.Except(sourceEntries, StringComparer.Ordinal).ToArray();
                // Definitions may contain sensitive constants; show object identities only.
                Console.WriteLine($"Schema inventory: {missing.Length} export entries absent/different; {extra.Length} Azure entries extra/different.");
                foreach (var entry in missing.Take(15)) Console.WriteLine("EXPORT-ONLY: " + TransferGuards.InventoryIdentity(entry));
                foreach (var entry in extra.Take(15)) Console.WriteLine("AZURE-ONLY: " + TransferGuards.InventoryIdentity(entry));
                var reportPath = Path.Combine(privateDir, $"Azure-diagnose-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.local.json");
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
                {
                    TargetServer, TargetDatabase, ReadAt = DateTimeOffset.UtcNow,
                    Expected = baseline, Actual = actual,
                    MissingSchemaEntries = missing, ExtraSchemaEntries = extra, Differences = differences
                }, jsonOptions));
                Console.WriteLine("Full metadata report saved privately in deploy-private (ignored by Git); do not share its contents blindly.");
            }
            else Console.WriteLine("Legacy baseline cannot be enriched. Backup and manifest remain unchanged.");
        }
    }
    TransferGuards.SameSnapshot(baseline, actual);
    Console.WriteLine("PASS: All table row counts and captured schema structures match the private local export baseline; tier unchanged.");
    Console.WriteLine("This verifies row counts and captured metadata, not every individual data value or API workflow.");
    Console.WriteLine("Recheck Free Offer / Overage Disabled in Azure portal. No local records were changed.");
}
catch (TransferFailure failure)
{
    Console.Error.WriteLine($"STOP: {failure.Message}");
    Environment.ExitCode = 1;
}
catch (SqlException failure)
{
    // Raw SQL/DacFx exceptions can contain credentials or record values. Do not print them.
    Console.Error.WriteLine($"STOP: SQL connection/query failed (number {failure.Number}). Check credentials, firewall and connectivity privately.");
    Environment.ExitCode = 1;
}
catch (Exception failure)
{
    Console.Error.WriteLine($"STOP: Operation failed ({failure.GetType().Name}); sensitive exception details are suppressed.");
    Console.Error.WriteLine("Do not retry import blindly: inspect the target for a partial import first. Preserve any partial export file.");
    Environment.ExitCode = 1;
}

static async Task<int> Scalar(SqlConnection sql, string query)
{
    await using var command = new SqlCommand(query, sql) { CommandTimeout = 120 };
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

static async Task<string> Tier(SqlConnection sql)
{
    await using var command = new SqlCommand("SELECT CONCAT(edition, '|', service_objective, '|', COALESCE(elastic_pool_name, '')) FROM sys.database_service_objectives WHERE database_id = DB_ID()", sql);
    return (string?)await command.ExecuteScalarAsync() ?? throw new TransferFailure("Cannot verify existing target tier.");
}

static async Task ShowObjects(SqlConnection sql)
{
    const string query = """
        SELECT type_desc, COUNT(*) FROM sys.objects WHERE is_ms_shipped=0 GROUP BY type_desc ORDER BY type_desc;
        SELECT TOP (15) s.name, o.name, o.type_desc FROM sys.objects o
        JOIN sys.schemas s ON s.schema_id=o.schema_id WHERE o.is_ms_shipped=0 ORDER BY s.name, o.type_desc, o.name;
        SELECT name FROM sys.schemas WHERE schema_id < 16384 AND name NOT IN ('dbo','guest','INFORMATION_SCHEMA','sys') ORDER BY name;
        SELECT s.name, t.name FROM sys.types t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_user_defined=1 ORDER BY s.name, t.name;
        """;
    await using var command = new SqlCommand(query, sql);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync()) Console.WriteLine($"OBJECT-COUNT: {reader.GetString(0)} = {reader.GetInt32(1)}");
    await reader.NextResultAsync();
    while (await reader.ReadAsync()) Console.WriteLine($"OBJECT: {reader.GetString(0)}.{reader.GetString(1)} ({reader.GetString(2)})");
    await reader.NextResultAsync();
    while (await reader.ReadAsync()) Console.WriteLine($"USER-SCHEMA: {reader.GetString(0)}");
    await reader.NextResultAsync();
    while (await reader.ReadAsync()) Console.WriteLine($"USER-TYPE: {reader.GetString(0)}.{reader.GetString(1)}");
}

static async Task<Snapshot> Capture(SqlConnection sql)
{
    var tables = new SortedDictionary<string, long>(StringComparer.Ordinal);
    var names = new List<(string Schema, string Table)>();
    await using (var command = new SqlCommand("SELECT s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.is_ms_shipped = 0 ORDER BY s.name, t.name", sql))
    await using (var reader = await command.ExecuteReaderAsync())
        while (await reader.ReadAsync()) names.Add((reader.GetString(0), reader.GetString(1)));
    foreach (var (schema, table) in names)
    {
        static string Quote(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
        await using var count = new SqlCommand($"SELECT COUNT_BIG(*) FROM {Quote(schema)}.{Quote(table)}", sql) { CommandTimeout = 120 };
        tables.Add(schema + "." + table, (long)(await count.ExecuteScalarAsync())!);
    }
    var inventory = new List<string>();
    // Compare portable schema facts, not engine-specific IDs or timestamps.
    const string query = """
        SELECT CONCAT('column|', s.name, '|', t.name, '|', c.column_id, '|', c.name, '|', ty.name, '|', c.max_length, '|', c.precision, '|', c.scale, '|', c.is_nullable, '|', c.is_identity)
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.columns c ON c.object_id=t.object_id JOIN sys.types ty ON ty.user_type_id=c.user_type_id
        WHERE t.is_ms_shipped=0
        UNION ALL
        SELECT CONCAT('index|', s.name, '|', t.name, '|', i.name, '|', i.is_unique, '|', i.is_primary_key, '|', ic.key_ordinal, '|', c.name, '|', ic.is_descending_key, '|', ic.is_included_column, '|', COALESCE(i.filter_definition,''))
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.indexes i ON i.object_id=t.object_id
        JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.is_ms_shipped=0 AND i.name IS NOT NULL
        UNION ALL
        SELECT CONCAT('fk|', s.name, '|', t.name, '|', fk.name, '|', c.name, '|', rs.name, '|', rt.name, '|', rc.name, '|', fk.delete_referential_action, '|', fk.update_referential_action, '|', fk.is_disabled, '|', fk.is_not_trusted)
        FROM sys.foreign_keys fk JOIN sys.tables t ON t.object_id=fk.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id JOIN sys.columns c ON c.object_id=fkc.parent_object_id AND c.column_id=fkc.parent_column_id
        JOIN sys.tables rt ON rt.object_id=fkc.referenced_object_id JOIN sys.schemas rs ON rs.schema_id=rt.schema_id JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fkc.referenced_column_id
        WHERE t.is_ms_shipped=0
        UNION ALL
        SELECT CONCAT('check|', s.name, '|', t.name, '|', cc.name, '|', cc.is_disabled, '|', cc.is_not_trusted, '|', cc.definition)
        FROM sys.check_constraints cc JOIN sys.tables t ON t.object_id=cc.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0
        """;
    await using (var command = new SqlCommand(query, sql))
    await using (var reader = await command.ExecuteReaderAsync())
        while (await reader.ReadAsync()) inventory.Add(reader.GetString(0));
    inventory.Sort(StringComparer.Ordinal);
    var generated = new List<string>();
    const string generatedQuery = """
        SELECT CONCAT('check|', s.name, '|', t.name, '|', c.name)
        FROM sys.check_constraints c JOIN sys.tables t ON t.object_id=c.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        WHERE t.is_ms_shipped=0 AND c.is_system_named=1
        UNION ALL
        SELECT CONCAT('fk|', s.name, '|', t.name, '|', c.name)
        FROM sys.foreign_keys c JOIN sys.tables t ON t.object_id=c.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        WHERE t.is_ms_shipped=0 AND c.is_system_named=1
        UNION ALL
        SELECT CONCAT('index|', s.name, '|', t.name, '|', i.name)
        FROM sys.key_constraints c JOIN sys.tables t ON t.object_id=c.parent_object_id JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.indexes i ON i.object_id=t.object_id AND i.index_id=c.unique_index_id
        WHERE t.is_ms_shipped=0 AND c.is_system_named=1
        """;
    await using (var command = new SqlCommand(generatedQuery, sql))
    await using (var reader = await command.ExecuteReaderAsync())
        while (await reader.ReadAsync()) generated.Add(reader.GetString(0));
    generated.Sort(StringComparer.Ordinal);
    return new(tables, inventory.Count, TransferGuards.InventoryDigest(inventory), inventory, generated);
}

static async Task<string> Hash(string path)
{
    await using var stream = File.OpenRead(path);
    return Convert.ToHexString(await SHA256.HashDataAsync(stream));
}

static void Show(string label, Snapshot snapshot) => Console.WriteLine(
    $"{label}: {snapshot.Tables.Count} tables, {snapshot.Tables.Values.Sum()} rows, {snapshot.InventoryCount} schema inventory entries. No patient/account values printed.");

static void Confirm(string prompt, string required)
{
    Console.WriteLine(prompt);
    if (Console.IsInputRedirected || Console.ReadLine() != required)
        throw new TransferFailure("Interactive confirmation missing. No transfer started.");
}

static string ReadPassword()
{
    Console.Write("Azure SQL password (hidden): ");
    var result = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (result.Length > 0) result.Length--; }
        else if (!char.IsControl(key.KeyChar)) result.Append(key.KeyChar);
    }
    Console.WriteLine();
    if (result.Length == 0) throw new TransferFailure("Password is required.");
    return result.ToString();
}

internal sealed record Snapshot(SortedDictionary<string, long> Tables, int InventoryCount, string InventoryHash,
    IReadOnlyList<string>? InventoryEntries = null, IReadOnlyList<string>? SystemNamedIdentities = null);
internal sealed record ExportManifest(string Source, string Server, string Database, DateTimeOffset ExportedAt, string Sha256, Snapshot Snapshot);
internal sealed class TransferFailure(string message) : Exception(message);
internal static class TransferGuards
{
    internal static void Source(SqlConnectionStringBuilder builder)
    {
        var host = builder.DataSource.Split('\\')[0].Split(',')[0];
        if (builder.InitialCatalog != "FoMedDb" || !(host is "." or "localhost" or "127.0.0.1" or "(local)"
            || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)))
            throw new TransferFailure("Source must be the configured local FoMedDb. Remote/system/other databases are blocked.");
    }
    internal static void EmptyTarget(int userObjects)
    {
        if (userObjects != 0) throw new TransferFailure("Target has user-defined objects. Import refuses to overwrite/reset it.");
    }
    internal static void SameSnapshot(Snapshot expected, Snapshot actual)
    {
        if (Differences(expected, actual).Count != 0)
            throw new TransferFailure("Schema inventory or row counts differ. Transfer is NOT verified; inspect privately, do not reset databases.");
    }
    internal static IReadOnlyList<string> Differences(Snapshot expected, Snapshot actual)
    {
        var result = new List<string>();
        foreach (var pair in expected.Tables)
        {
            if (!actual.Tables.TryGetValue(pair.Key, out var count)) result.Add($"Missing table {pair.Key} (export has {pair.Value} rows).");
            else if (count != pair.Value) result.Add($"Row count {pair.Key}: export={pair.Value}, Azure={count}.");
        }
        foreach (var pair in actual.Tables)
            if (!expected.Tables.ContainsKey(pair.Key)) result.Add($"Extra table {pair.Key} (Azure has {pair.Value} rows).");
        if (expected.InventoryCount != actual.InventoryCount)
            result.Add($"Schema inventory count: export={expected.InventoryCount}, Azure={actual.InventoryCount}.");
        if (expected.InventoryHash != actual.InventoryHash && !SameStructure(expected, actual))
            result.Add("Schema inventory hash differs (columns/indexes/FKs/checks); raw definitions are not printed.");
        return result;
    }
    internal static string InventoryDigest(IEnumerable<string> entries) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', entries.Order(StringComparer.Ordinal)))));
    internal static bool SameStructure(Snapshot expected, Snapshot actual)
    {
        // No regex guessing: SQL's is_system_named flags are the evidence.
        if (expected.InventoryEntries is null || actual.InventoryEntries is null
            || expected.SystemNamedIdentities is null || actual.SystemNamedIdentities is null) return false;
        return NormalizeInventory(expected).SequenceEqual(NormalizeInventory(actual), StringComparer.Ordinal);
    }
    internal static IReadOnlyList<string> NormalizeInventory(Snapshot snapshot)
    {
        var entries = snapshot.InventoryEntries ?? throw new TransferFailure("Inventory entries are required for normalization.");
        if (entries.Count != snapshot.InventoryCount || InventoryDigest(entries) != snapshot.InventoryHash)
            throw new TransferFailure("Inventory entries do not match the saved raw fingerprint. Normalization is blocked.");
        var generated = new HashSet<string>(snapshot.SystemNamedIdentities
            ?? throw new TransferFailure("SQL system-name metadata is required."), StringComparer.Ordinal);
        var normalized = new List<string>();
        foreach (var group in entries.GroupBy(InventoryIdentity, StringComparer.Ordinal))
        {
            var kind = group.Key.Split('|')[0];
            if (!generated.Contains(group.Key) || kind is not ("index" or "fk" or "check"))
            { normalized.AddRange(group); continue; }
            // Hash each COMPLETE constraint/index group without its generated name.
            // This preserves multi-column grouping/order and duplicate multiplicity.
            var parts = group.Select(entry => entry.Split('|')).ToArray();
            var structure = parts.Select(p => string.Join('|', p.Take(3).Concat(p.Skip(4))));
            var name = "<sql-system-named:" + InventoryDigest(structure) + ">";
            foreach (var part in parts)
            {
                part[3] = name;
                normalized.Add(string.Join('|', part));
            }
        }
        normalized.Sort(StringComparer.Ordinal);
        return normalized;
    }
    internal static string InventoryIdentity(string entry)
    {
        var parts = entry.Split('|');
        var count = parts[0] == "column" ? 5 : 4;
        return string.Join('|', parts.Take(count));
    }
}
internal static class TransferTests
{
    internal static void Run()
    {
        var checks = 0;
        void Reject(Action action)
        {
            try { action(); } catch (TransferFailure) { checks++; return; }
            throw new Exception("Guard accepted an unsafe input.");
        }
        foreach (var server in new[] { ".", "localhost", "127.0.0.1", Environment.MachineName, ".\\SQLEXPRESS" })
        { TransferGuards.Source(new() { DataSource=server, InitialCatalog="FoMedDb" }); checks++; }
        foreach (var database in new[] { "master", "tempdb", "FoMedDbDemo", "Other" })
            Reject(() => TransferGuards.Source(new() { DataSource="localhost", InitialCatalog=database }));
        foreach (var server in new[] { "remote", "example.database.windows.net", "localhost.evil.test" })
            Reject(() => TransferGuards.Source(new() { DataSource=server, InitialCatalog="FoMedDb" }));
        TransferGuards.EmptyTarget(0); checks++;
        Reject(() => TransferGuards.EmptyTarget(1));
        var first = new Snapshot(new() { ["auth.users"] = 5 }, 4, "abc");
        TransferGuards.SameSnapshot(first, new(new() { ["auth.users"] = 5 }, 4, "abc")); checks++;
        Reject(() => TransferGuards.SameSnapshot(first, new(new() { ["auth.users"] = 6 }, 4, "abc")));
        Reject(() => TransferGuards.SameSnapshot(first, new(new() { ["auth.users"] = 5 }, 4, "different")));
        Reject(() => TransferGuards.SameSnapshot(first, new(new() { ["other.users"] = 5 }, 4, "abc")));
        Reject(() => TransferGuards.SameSnapshot(first, new(new() { ["auth.users"] = 5 }, 5, "abc")));
        if (TransferGuards.Differences(first, new(new() { ["auth.users"] = 0 }, 4, "abc"))
            is not ["Row count auth.users: export=5, Azure=0."]) throw new Exception("Row-count diagnostic failed.");
        checks++;
        if (TransferGuards.Differences(first, new(new() { ["auth.users"] = 5 }, 4, "abc")).Count != 0)
            throw new Exception("Equal snapshots reported differences.");
        checks++;
        if (!TransferGuards.Differences(first, new(new() { ["other.users"] = 5 }, 4, "abc")).Any(x => x.StartsWith("Missing table")))
            throw new Exception("Missing table diagnostic failed.");
        checks++;
        if (!TransferGuards.Differences(first, new(new() { ["other.users"] = 5 }, 4, "abc")).Any(x => x.StartsWith("Extra table")))
            throw new Exception("Extra table diagnostic failed.");
        checks++;
        if (TransferGuards.InventoryIdentity("check|auth|users|CK_test|0|0|private-constant") != "check|auth|users|CK_test")
            throw new Exception("Diagnostic printed sensitive schema constants.");
        checks++;
        if (TransferGuards.InventoryIdentity("column|auth|users|1|id|int|4|10|0|0|1") != "column|auth|users|1|id")
            throw new Exception("Column diagnostic identity failed.");
        checks++;
        static Snapshot Schema(string[] entries, string[] generated) => new(new() { ["auth.users"] = 5 },
            entries.Length, TransferGuards.InventoryDigest(entries), entries, generated);
        void Match(Snapshot expected, Snapshot actual)
        { TransferGuards.SameSnapshot(expected, actual); checks++; }
        var checkA = "check|auth|users|CK__users__active__11111111|0|0|([is_active]=(0) OR [is_active]=(1))";
        var checkB = checkA.Replace("11111111", "22222222", StringComparison.Ordinal);
        var checkSource = Schema([checkA], ["check|auth|users|CK__users__active__11111111"]);
        var checkAzure = Schema([checkB], ["check|auth|users|CK__users__active__22222222"]);
        Match(checkSource, checkAzure);
        Reject(() => TransferGuards.SameSnapshot(checkSource, Schema([checkB], [])));
        Reject(() => TransferGuards.SameSnapshot(Schema([checkA], []), checkAzure));
        Reject(() => TransferGuards.SameSnapshot(Schema([checkA], []), Schema([checkB], [])));
        Reject(() => TransferGuards.SameSnapshot(checkSource, checkAzure with { Tables=new() { ["auth.users"]=4 } }));
        foreach (var changed in new[]
        {
            checkB.Replace("|0|0|(", "|1|0|(", StringComparison.Ordinal), // disabled
            checkB.Replace("|0|0|(", "|0|1|(", StringComparison.Ordinal), // untrusted
            checkB.Replace("=(1)", "=(2)", StringComparison.Ordinal) // different rule
        }) Reject(() => TransferGuards.SameSnapshot(checkSource, Schema([changed], checkAzure.SystemNamedIdentities!.ToArray())));
        var fkA = "fk|auth|users|FK__users__role__11111111|role_id|auth|roles|id|0|0|0|0";
        var fkB = fkA.Replace("11111111", "22222222", StringComparison.Ordinal);
        var fkSource = Schema([fkA], ["fk|auth|users|FK__users__role__11111111"]);
        var fkAzure = Schema([fkB], ["fk|auth|users|FK__users__role__22222222"]);
        Match(fkSource, fkAzure);
        Reject(() => TransferGuards.SameSnapshot(fkSource, Schema([fkB.Replace("|roles|", "|other_roles|", StringComparison.Ordinal)], fkAzure.SystemNamedIdentities!.ToArray())));
        Reject(() => TransferGuards.SameSnapshot(fkSource, Schema([fkB[..^7] + "1|0|0|0"], fkAzure.SystemNamedIdentities!.ToArray())));
        var indexA = "index|auth|users|PK__users__11111111|1|1|1|id|0|0|";
        var indexB = indexA.Replace("11111111", "22222222", StringComparison.Ordinal);
        var indexSource = Schema([indexA], ["index|auth|users|PK__users__11111111"]);
        var indexAzure = Schema([indexB], ["index|auth|users|PK__users__22222222"]);
        Match(indexSource, indexAzure);
        Reject(() => TransferGuards.SameSnapshot(indexSource, Schema([indexB.Replace("|1|1|1|id", "|0|1|1|id", StringComparison.Ordinal)], indexAzure.SystemNamedIdentities!.ToArray())));
        Reject(() => TransferGuards.SameSnapshot(indexSource, Schema([indexB.Replace("|id|", "|other_id|", StringComparison.Ordinal)], indexAzure.SystemNamedIdentities!.ToArray())));
        Reject(() => TransferGuards.SameSnapshot(checkSource, checkAzure with { InventoryHash="corrupted" }));
        Reject(() => TransferGuards.SameSnapshot(checkSource, checkAzure with { InventoryCount=2 }));
        var namedA = "index|auth|users|IX_explicit|0|0|1|id|0|0|";
        var namedB = namedA.Replace("IX_explicit", "IX_renamed", StringComparison.Ordinal);
        Reject(() => TransferGuards.SameSnapshot(Schema([namedA], []), Schema([namedB], [])));
        static string Composite(string name, int ordinal, string column) => $"index|auth|users|{name}|0|0|{ordinal}|{column}|0|0|";
        var groupedSource = Schema([Composite("A", 1, "a"), Composite("A", 2, "b"), Composite("B", 1, "c"), Composite("B", 2, "d")],
            ["index|auth|users|A", "index|auth|users|B"]);
        var regrouped = Schema([Composite("C", 1, "a"), Composite("C", 2, "d"), Composite("D", 1, "c"), Composite("D", 2, "b")],
            ["index|auth|users|C", "index|auth|users|D"]);
        Reject(() => TransferGuards.SameSnapshot(groupedSource, regrouped));
        Match(groupedSource, Schema([Composite("C", 1, "a"), Composite("C", 2, "b"), Composite("D", 1, "c"), Composite("D", 2, "d")],
            ["index|auth|users|C", "index|auth|users|D"]));
        Console.WriteLine($"PASS: {checks} transfer guard checks; no database connection or file export.");
    }
}
