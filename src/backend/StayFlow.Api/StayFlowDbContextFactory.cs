using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef migrations add/update).
/// Forces the PostgreSQL provider regardless of environment variables so that
/// migration scaffolding works without a running application or database.
/// </summary>
public class StayFlowDbContextFactory : IDesignTimeDbContextFactory<StayFlowDbContext>
{
    public StayFlowDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=stayflow_db;Username=stayflow_app;Password=stayflow_dev_password_change_in_prod;";

        var optionsBuilder = new DbContextOptionsBuilder<StayFlowDbContext>();
        optionsBuilder.UseNpgsql(connectionString,
            b => b.MigrationsAssembly("StayFlow.Api"));

        return new StayFlowDbContext(optionsBuilder.Options);
    }
}
