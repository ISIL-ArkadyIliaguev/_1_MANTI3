using Manti3.Backend.Domain.Items;
using Microsoft.EntityFrameworkCore;

namespace Manti3.Backend.Infrastructure.Persistence;

// קריאה בלבד: אין מיגרציות, ה-DB בבעלות MANTI 2
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<HoningPeripheralItem> HoningPeripheralItems => Set<HoningPeripheralItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
