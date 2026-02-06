using System.Data.Entity;
using ITSM_WS.Models;

namespace ITSM_WS.Data
{
    public class IdempotencyDbContext : DbContext
    {
        public IdempotencyDbContext()
            : base("name=IdempotencyDB")
        {
        }

        public DbSet<IdempotencyRequest> IdempotencyRequests { get; set; }

        public DbSet<IdempotencyEvent> IdempotencyEvents { get; set; }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<IdempotencyRequest>()
                .HasIndex(tbl => new { tbl.IdempotencyKey, tbl.Endpoint })
                .IsUnique();
        }
    }
}