using System;
using System.Configuration;
using System.Web.Services;
using ITSM_WS.Data.Data;
using ITSM_WS.Services;

namespace ITSM_WS
{
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]

    public class ITSM_WS : WebService
    {
        private readonly ExeconService      _execonService;
        private readonly IdempotencyService _idempotencyService;
        private readonly ScheduleService    _scheduleService;

        public ITSM_WS()
        {
            var db = new IdempotencyDbContext();

            // 👉 decidir entorno aquí
            var env = ConfigurationManager.AppSettings["Environment"];
            var ambiente = (env == "PRD")
                ? ConfigurationManager.AppSettings["Execon.BaseUrl.Prod"]
                : ConfigurationManager.AppSettings["Execon.BaseUrl.Dev"];

            _execonService      = new ExeconService(ConfigurationManager.AppSettings["ITSMServiceUrl"], ambiente);
            _idempotencyService = new IdempotencyService(db);
            _scheduleService    = new ScheduleService(_idempotencyService, _execonService);
        }

        [WebMethod]
        public string CallExternalApi(int assignmentId, string recId)
        {
            return _scheduleService.ProcessRequest(assignmentId, recId)
                .GetAwaiter()
                .GetResult();
        }
    }
}