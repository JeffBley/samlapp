using System.Security.Cryptography;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SamlSample.Web.Data;
using SamlSample.Web.Models;

namespace SamlSample.Web.Services;

public class BootstrapDataService(
    AppDbContext dbContext,
    IWebHostEnvironment environment,
    ISecretHashingService secretHashingService,
    ILogger<BootstrapDataService> logger)
{
    public async Task EnsureInitializedAsync(string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""LocalAdminCredentials"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_LocalAdminCredentials"" PRIMARY KEY AUTOINCREMENT,
                ""Username"" TEXT NOT NULL,
                ""PasswordHash"" TEXT NOT NULL,
                ""Salt"" TEXT NOT NULL,
                ""IsEnabled"" INTEGER NOT NULL,
                ""CreatedUtc"" TEXT NOT NULL
            );",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LocalAdminCredentials_Username"" ON ""LocalAdminCredentials"" (""Username"");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""ConfigChangeLogs"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ConfigChangeLogs"" PRIMARY KEY,
                ""Section"" TEXT NOT NULL,
                ""FieldName"" TEXT NOT NULL,
                ""BeforeValue"" TEXT NOT NULL,
                ""AfterValue"" TEXT NOT NULL,
                ""ChangedBy"" TEXT NOT NULL,
                ""ChangedUtc"" TEXT NOT NULL
            );",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX IF NOT EXISTS ""IX_ConfigChangeLogs_ChangedUtc"" ON ""ConfigChangeLogs"" (""ChangedUtc"");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""SignInLogs"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SignInLogs"" PRIMARY KEY,
                ""UserName"" TEXT NOT NULL,
                ""CertThumbprint"" TEXT NOT NULL,
                ""SignedInUtc"" TEXT NOT NULL
            );",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX IF NOT EXISTS ""IX_SignInLogs_SignedInUtc"" ON ""SignInLogs"" (""SignedInUtc"");",
            cancellationToken);

        // GlobalSettings table (single-row global config)
        await dbContext.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""GlobalSettings"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_GlobalSettings"" PRIMARY KEY AUTOINCREMENT,
                ""LogRetentionDays"" INTEGER NOT NULL DEFAULT 14,
                ""UpdatedUtc"" TEXT NOT NULL
            );",
            cancellationToken);

        // Add new columns to existing tables
        await EnsureColumnAsync("SamlSettings", "AppName", @"ALTER TABLE ""SamlSettings"" ADD COLUMN ""AppName"" TEXT NOT NULL DEFAULT 'Default';", cancellationToken);
        await EnsureColumnAsync("SamlCertificates", "AppId", @"ALTER TABLE ""SamlCertificates"" ADD COLUMN ""AppId"" INTEGER NOT NULL DEFAULT 1;", cancellationToken);
        await EnsureColumnAsync("SignInLogs", "AppName", @"ALTER TABLE ""SignInLogs"" ADD COLUMN ""AppName"" TEXT NOT NULL DEFAULT '';", cancellationToken);
        await EnsureColumnAsync("SamlSettings", "UrlSlug", @"ALTER TABLE ""SamlSettings"" ADD COLUMN ""UrlSlug"" TEXT NOT NULL DEFAULT '';", cancellationToken);
        await EnsureColumnAsync("SamlSettings", "IsSystem", @"ALTER TABLE ""SamlSettings"" ADD COLUMN ""IsSystem"" INTEGER NOT NULL DEFAULT 0;", cancellationToken);

        // Seed GlobalSettings – migrate LogRetentionDays from old SamlSettings column if present
        var globalSettings = await dbContext.GlobalSettings.SingleOrDefaultAsync(cancellationToken);
        if (globalSettings is null)
        {
            var retentionDays = await ReadOldLogRetentionDaysAsync(cancellationToken);
            dbContext.GlobalSettings.Add(new GlobalSettings { LogRetentionDays = retentionDays, UpdatedUtc = DateTimeOffset.UtcNow });
            await dbContext.SaveChangesAsync(cancellationToken);
            globalSettings = await dbContext.GlobalSettings.SingleAsync(cancellationToken);
        }

        if (globalSettings.LogRetentionDays <= 0)
        {
            globalSettings.LogRetentionDays = 14;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Drop retired columns (LogRetentionDays migrated to GlobalSettings above)
        await DropColumnIfExistsAsync("SamlSettings", "LogRetentionDays", cancellationToken);
        await DropColumnIfExistsAsync("SamlCertificates", "IsPrimary", cancellationToken);
        await DropColumnIfExistsAsync("SamlSettings", "DailyRunUtcTime", cancellationToken);

        // Fix certificate index: old per-thumbprint unique → new per-(AppId, Thumbprint) unique
        await dbContext.Database.ExecuteSqlRawAsync(@"DROP INDEX IF EXISTS ""IX_SamlCertificates_Thumbprint"";", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SamlCertificates_AppId_Thumbprint"" ON ""SamlCertificates"" (""AppId"", ""Thumbprint"");", cancellationToken);

        // Backfill UrlSlug for any existing rows that don't have one yet
        await BackfillUrlSlugsAsync(cancellationToken);

        // Clear legacy single-app ACS URLs (e.g. "/saml/acs") so slug-based auto-URL is used
        await ClearLegacyAcsUrlsAsync(cancellationToken);

        // Backfill SpEntityId for apps that don't have one set yet
        await BackfillEntityIdsAsync(cancellationToken);

        // Ensure the SAML Launcher system app exists
        await EnsureSamlLauncherAppAsync(baseUrl, cancellationToken);

        var localAdminCredential = await dbContext.LocalAdminCredentials.SingleOrDefaultAsync(c => c.Username == "bootstrap-admin", cancellationToken);
        if (localAdminCredential is null)
        {
            var generatedPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));
            var (hash, salt) = secretHashingService.HashSecret(generatedPassword);

            dbContext.LocalAdminCredentials.Add(new LocalAdminCredential
            {
                Username = "bootstrap-admin",
                PasswordHash = hash,
                Salt = salt,
                IsEnabled = true
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            var internalFolder = Path.Combine(environment.ContentRootPath, ".internal");
            Directory.CreateDirectory(internalFolder);
            var adminSecretFilePath = Path.Combine(internalFolder, "bootstrap-admin-credentials.txt");
            if (!File.Exists(adminSecretFilePath))
            {
                var content = $"url=/bootstrap-admin/login{Environment.NewLine}username=bootstrap-admin{Environment.NewLine}password={generatedPassword}{Environment.NewLine}generatedUtc={DateTimeOffset.UtcNow:O}{Environment.NewLine}";
                await File.WriteAllTextAsync(adminSecretFilePath, content, cancellationToken);
            }

            logger.LogWarning("A bootstrap local admin credential was generated and written to {AdminSecretFilePath}. Disable this account after SAML onboarding.", adminSecretFilePath);
        }
    }

    private async Task EnsureSamlLauncherAppAsync(string? baseUrl, CancellationToken cancellationToken)
    {
        const string systemEntityId = "urn:samlsample:web:0";
        const string systemSlug = "saml-launcher";
        const string systemName = "SAML Launcher - Admin Portal";
        var expectedAcsUrl = string.IsNullOrEmpty(baseUrl)
            ? string.Empty
            : $"{baseUrl.TrimEnd('/')}/saml/{systemSlug}/acs";

        var launcher = await dbContext.SamlSettings
            .SingleOrDefaultAsync(s => s.SpEntityId == systemEntityId || s.IsSystem, cancellationToken);

        if (launcher is null)
        {
            launcher = new SamlSettings
            {
                AppName = systemName,
                SpEntityId = systemEntityId,
                UrlSlug = systemSlug,
                AssertionConsumerServiceUrl = expectedAcsUrl,
                IsSystem = true
            };
            dbContext.SamlSettings.Add(launcher);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var dirty = false;
            if (!launcher.IsSystem) { launcher.IsSystem = true; dirty = true; }
            if (launcher.SpEntityId != systemEntityId) { launcher.SpEntityId = systemEntityId; dirty = true; }
            if (launcher.UrlSlug != systemSlug) { launcher.UrlSlug = systemSlug; dirty = true; }
            if (launcher.AppName != systemName) { launcher.AppName = systemName; dirty = true; }
            // Fill in ACS URL if it's empty and we have a base URL
            if (!string.IsNullOrEmpty(expectedAcsUrl) && string.IsNullOrEmpty(launcher.AssertionConsumerServiceUrl))
            {
                launcher.AssertionConsumerServiceUrl = expectedAcsUrl;
                dirty = true;
            }
            if (dirty) await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task BackfillUrlSlugsAsync(CancellationToken cancellationToken)
    {
        var apps = await dbContext.SamlSettings
            .Where(s => s.UrlSlug == string.Empty)
            .ToListAsync(cancellationToken);

        foreach (var app in apps)
        {
            app.UrlSlug = GenerateSlugFromName(app.AppName);
        }

        if (apps.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task BackfillEntityIdsAsync(CancellationToken cancellationToken)
    {
        var apps = await dbContext.SamlSettings
            .Where(s => s.SpEntityId == string.Empty)
            .ToListAsync(cancellationToken);

        foreach (var app in apps)
        {
            app.SpEntityId = $"urn:samlsample:web:{app.Id}";
        }

        if (apps.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    // Clears stored ACS URLs that match the old single-app pattern (/saml/acs) so the
    // slug-based URL is auto-generated instead. Safe to run on every startup — it only
    // clears URLs that end with the legacy path segment and leaves custom ones untouched.
    private async Task ClearLegacyAcsUrlsAsync(CancellationToken cancellationToken)
    {
        var apps = await dbContext.SamlSettings
            .Where(s => s.AssertionConsumerServiceUrl.EndsWith("/saml/acs"))
            .ToListAsync(cancellationToken);

        foreach (var app in apps)
        {
            app.AssertionConsumerServiceUrl = string.Empty;
        }

        if (apps.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Cleared {Count} legacy ACS URL(s) that used the old /saml/acs path.", apps.Count);
        }
    }

    private static string GenerateSlugFromName(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[\s_]+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-{2,}", "-");
        return slug.Trim('-');
    }

    private async Task<int> ReadOldLogRetentionDaysAsync(CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // Check if the column still exists before reading it
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(\"SamlSettings\")";
        await using var checkReader = await checkCmd.ExecuteReaderAsync(cancellationToken);
        var hasColumn = false;
        while (await checkReader.ReadAsync(cancellationToken))
        {
            if (string.Equals(checkReader[1]?.ToString(), "LogRetentionDays", StringComparison.OrdinalIgnoreCase))
            {
                hasColumn = true;
                break;
            }
        }

        await checkReader.CloseAsync();

        if (!hasColumn)
        {
            return 14;
        }

        await using var readCmd = connection.CreateCommand();
        readCmd.CommandText = "SELECT LogRetentionDays FROM \"SamlSettings\" LIMIT 1";
        var result = await readCmd.ExecuteScalarAsync(cancellationToken);
        if (result is not null && int.TryParse(result.ToString(), out var days) && days > 0)
        {
            return days;
        }

        return 14;
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string alterSql, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var exists = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader[1]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        await reader.CloseAsync();

        if (!exists)
        {
            await dbContext.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
        }
    }

    private async Task DropColumnIfExistsAsync(string tableName, string columnName, CancellationToken cancellationToken)
    {
        await using var connection = dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var exists = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader[1]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        await reader.CloseAsync();

        if (exists)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\";",
                cancellationToken);
        }
    }
}
