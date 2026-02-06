using System.Data.Entity;
using System.Web.Http;
using ITSM_WS.Data;

namespace ITSM_WS
{
    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            Database.SetInitializer<IdempotencyDbContext>(null);
        }
    }
}
