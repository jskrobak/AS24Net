using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;


namespace AS24Net.Entity;

public class A24DesignTimeDbContextFactory:IDesignTimeDbContextFactory<A24DbContext>
{
    public A24DbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Entity.json")
            .AddJsonFile($"appsettings.Entity.{environment}.json", true)
            .AddJsonFile($"appsettings.Entity.{environment}.local.json", true) // .gitignored
            .Build();

        var connectionString = configuration.GetConnectionString("AS24Net");
        
        var builder = new DbContextOptionsBuilder<A24DbContext>();
        
        builder.UseNpgsql(connectionString);
        
        return new A24DbContext(builder.Options);
    }
}