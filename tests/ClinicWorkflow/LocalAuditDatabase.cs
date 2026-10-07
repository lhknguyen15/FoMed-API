using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

// Intentionally no configurable server/credentials: audits must never use Azure or FoMedDb.
internal static class LocalAuditDatabase
{
    internal static SqlConnectionStringBuilder Master(string databaseName)
    {
        var connection = new SqlConnectionStringBuilder
        {
            DataSource = "localhost", InitialCatalog = "master", IntegratedSecurity = true,
            TrustServerCertificate = true, ConnectTimeout = 5
        };
        Guard(connection, databaseName);
        return connection;
    }

    internal static void Guard(SqlConnectionStringBuilder connection, string databaseName)
    {
        if (connection.DataSource != "localhost" || !connection.IntegratedSecurity
            || !Regex.IsMatch(databaseName, "^FoMed_(Audit|Test)_[a-f0-9]{32}$")
            || (connection.InitialCatalog != "master" && connection.InitialCatalog != databaseName))
            throw new InvalidOperationException("Only a uniquely named localhost audit database is allowed.");
    }
}
