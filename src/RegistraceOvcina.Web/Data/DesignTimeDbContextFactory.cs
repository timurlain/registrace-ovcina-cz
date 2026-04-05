using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RegistraceOvcina.Web.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql("Host=127.0.0.1;Port=5433;Database=registrace_ovcina_development;Username=postgres;Password=postgres");
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
