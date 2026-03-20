using Microsoft.EntityFrameworkCore;
using SamlSample.Web.Models;

namespace SamlSample.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SamlSettings> SamlSettings => Set<SamlSettings>();
    public DbSet<GlobalSettings> GlobalSettings => Set<GlobalSettings>();
    public DbSet<SamlCertificate> SamlCertificates => Set<SamlCertificate>();
    public DbSet<LocalAdminCredential> LocalAdminCredentials => Set<LocalAdminCredential>();
    public DbSet<ConfigChangeLog> ConfigChangeLogs => Set<ConfigChangeLog>();
    public DbSet<SignInLog> SignInLogs => Set<SignInLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Composite unique index: same cert thumbprint can appear once per app
        modelBuilder.Entity<SamlCertificate>()
            .HasIndex(c => new { c.AppId, c.Thumbprint })
            .IsUnique();

        modelBuilder.Entity<LocalAdminCredential>()
            .HasIndex(c => c.Username)
            .IsUnique();

        modelBuilder.Entity<ConfigChangeLog>()
            .HasIndex(c => c.ChangedUtc);

        modelBuilder.Entity<SignInLog>()
            .HasIndex(s => s.SignedInUtc);
    }
}
