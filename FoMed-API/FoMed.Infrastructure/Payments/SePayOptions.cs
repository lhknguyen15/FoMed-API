using System.Text.RegularExpressions;

namespace FoMed.Infrastructure.Payments;

public sealed class SePayOptions
{
    public const string SectionName = "SePay";
    public bool Enabled { get; set; }
    public string Environment { get; set; } = "Test";
    public bool AllowLivePayments { get; set; }
    // Operator acknowledgement that this exact database contains only disposable demo data.
    // A database name is not proof of its contents; never set this for real patient/payment data.
    public string TestDatabaseName { get; set; } = "";
    public string BankCode { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public int RequestLifetimeMinutes { get; set; } = 15;
    public int SignatureToleranceSeconds { get; set; } = 300;

    public bool CanUseDatabase(string database) => !Enabled ||
        (!string.IsNullOrWhiteSpace(database) && database == database.Trim() && !new[] { "master", "model", "msdb", "tempdb" }.Contains(database, StringComparer.OrdinalIgnoreCase)
            && (Environment == "Test"
                ? !string.IsNullOrWhiteSpace(TestDatabaseName) && string.Equals(database, TestDatabaseName, StringComparison.OrdinalIgnoreCase)
                : Environment == "Live" && !string.Equals(database, TestDatabaseName, StringComparison.OrdinalIgnoreCase)));

    public bool IsValid() => !Enabled ||
        (Environment is "Test" or "Live" && (Environment != "Live" || AllowLivePayments)
        && (Environment != "Test" || (!string.IsNullOrWhiteSpace(TestDatabaseName) && TestDatabaseName.Length <= 128
            && TestDatabaseName == TestDatabaseName.Trim()
            && !new[] { "master", "model", "msdb", "tempdb" }.Contains(TestDatabaseName, StringComparer.OrdinalIgnoreCase)))
        && Regex.IsMatch(BankCode, "^[A-Za-z0-9_.-]{1,64}$")
        && Gateway.Length is > 0 and <= 64 && Gateway == Gateway.Trim()
        && Regex.IsMatch(AccountNumber, "^[A-Za-z0-9]{1,34}$")
        && !string.IsNullOrWhiteSpace(AccountName) && AccountName.Length <= 255
        && !string.IsNullOrWhiteSpace(WebhookSecret) && WebhookSecret.Length >= 32 && WebhookSecret.Length <= 512
        && RequestLifetimeMinutes is >= 1 and <= 60 && SignatureToleranceSeconds is >= 30 and <= 300);
}
