using Microsoft.EntityFrameworkCore;

namespace Seckill_dotnet.Infrastructure
{
    public class SeckillContext : DbContext
    {
        public SeckillContext(DbContextOptions<SeckillContext> options) : base(options)
        {
        }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.ProductId }).IsUnique();
            });
        }
    }

}
