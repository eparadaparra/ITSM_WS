using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using ITSM_WS.Data.Models;

namespace ITSM_WS.Data.Data
{
    public class IdempotencyDbContext : DbContext
    {
        public IdempotencyDbContext() : base("name=IdempotencyConnection")
        {
            // Configurar timeout de comando (30 segundos)
            var objectContext = (this as IObjectContextAdapter).ObjectContext;
            objectContext.CommandTimeout = 30;

            // Deshabilitar lazy loading
            this.Configuration.LazyLoadingEnabled = false;
            this.Configuration.ProxyCreationEnabled = false;
        }

        public DbSet<IdempotencyRequest> IdempotencyRequests { get; set; }
        public DbSet<IdempotencyEvent> IdempotencyEvents { get; set; }

    }
}