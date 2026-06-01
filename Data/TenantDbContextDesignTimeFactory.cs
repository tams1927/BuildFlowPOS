using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HardwareManagementSystem.Data
{
    /// <summary>
    /// Design-time factory used ONLY by the EF Core tooling (dotnet ef) to build
    /// <see cref="TenantDbContext"/> when generating or scripting migrations.
    ///
    /// This is NOT used at runtime and does NOT register TenantDbContext in DI.
    /// The placeholder connection string is never connected to during
    /// "migrations add" — it only needs to be a syntactically valid SQL Server
    /// connection string so the provider can build the model.
    /// </summary>
    public class TenantDbContextDesignTimeFactory : IDesignTimeDbContextFactory<TenantDbContext>
    {
        public TenantDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<TenantDbContext>()
                .UseSqlServer(
                    "Server=localhost;Database=HardBuild_Tenant_DesignTime;" +
                    "Trusted_Connection=True;TrustServerCertificate=True;" +
                    "MultipleActiveResultSets=true")
                .Options;

            return new TenantDbContext(options);
        }
    }
}
