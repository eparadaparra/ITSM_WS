using System;
using System.Configuration;
using System.Threading.Tasks;
using System.Web.Services;

using ITSM_WS.Data;
using ITSM_WS.Models;
using ITSM_WS.Services;

namespace ITSM_WS
{
    [WebService(Namespace = "http://tempuri.org/")]
    [WebServiceBinding(ConformsTo = WsiProfiles.BasicProfile1_1)]
    
    public class ITSM_WS : WebService
    {
        private readonly IdempotencyService _idempotencyService;
        private readonly ExeconService _execonService;

        public ITSM_WS()
        {
            var db = new IdempotencyDbContext();
            _idempotencyService = new IdempotencyService(db);

            // 👉 decidir entorno aquí
            var env = ConfigurationManager.AppSettings["Environment"];
            var ambiente = (env == "PRD")
                ? ConfigurationManager.AppSettings["Execon.BaseUrl.Prod"]
                : ConfigurationManager.AppSettings["Execon.BaseUrl.Dev"];

            _execonService = new ExeconService(ConfigurationManager.AppSettings["ITSMServiceUrl"], ambiente);
        }

        [WebMethod]
        public async Task<string> CallExternalApi(int assignmentId, string recId)
        {
            const string endpoint = "CallExternalApi";
            var hash = GenerateRequestHash(assignmentId, recId);

            //var existing = await _idempotencyService.GetAsync(recId, endpoint);
            var request = await _idempotencyService.StartOrGetAsync(recId, endpoint, hash);

            if (request.RequestHash != hash)
            {
                await _idempotencyService.MarkDeadAsync(request, "Idempotency-Key reused with different payload");
                return "Solicitud inválida: Idempotency-Key reutilizado con datos distintos.";
            }

            #region 🚫 Estados bloqueantes
            // 1️⃣ Ya completado
            if (request.Status == IdempotencyStatus.Completed)
                return request.Response ?? "Solicitud ya procesada.";
            // 2️⃣ InProgress activo
            if (request.Status == IdempotencyStatus.InProgress)
            {
                var timeout = int.Parse( ConfigurationManager.AppSettings["Idempotency.InProgressTimeoutMinutes"] );

                if (!_idempotencyService.IsInProgressExpired(request, timeout))
                    return "Solicitud en proceso. Intenta más tarde.";

                // Intentar takeover
                var taken = await _idempotencyService.TryTakeoverAsync(request);
                if (!taken)
                    return "Otro proceso retomó la solicitud.";
            }

            // 3️⃣ Failed → retry
            if (request.Status == IdempotencyStatus.Failed)
            {
                var maxAttempts = int.Parse(ConfigurationManager.AppSettings["Idempotency.MaxAttempts"]);

                if (!_idempotencyService.CanRetry(request, maxAttempts))
                {
                    await _idempotencyService.MarkDeadAsync(request, "Max retries exceeded");
                    return "Solicitud marcada como Dead.";
                }

                if (!_idempotencyService.BackoffElapsed(request))
                    return "Backoff activo. Reintenta más tarde.";
            }
            #endregion

            bool success = false;
            string responseMessage;
            var idempotencyKey = $"{assignmentId}:{recId}";
            var lockAcquired = await _idempotencyService.TryAcquireLockAsync(request);
            if (!lockAcquired)
            {
                return "Solicitud ya está siendo procesada por otro proceso.";
            }
            
            try
            {
                // Procesar la tarea con ExeconService
                success = await _execonService.ScheduleTaskAsync(assignmentId, idempotencyKey);
                responseMessage = success
                    ? $"Tarea {assignmentId} ejecutada correctamente."
                    : $"Error al ejecutar tarea {assignmentId}.";

                if (success)
                {
                    await _idempotencyService.CompleteAsync(request, true, responseMessage);
                }
                else
                {
                    await _idempotencyService.MarkFailedAsync(request, responseMessage);
                }

                return responseMessage;
            }
            catch (Exception ex)
            {
                success = false;
                await _idempotencyService.MarkFailedAsync(request, $"Excepción al ejecutar tarea: {ex.Message}");
                
                return $"Error al ejecutar tarea {assignmentId}";
            }
        }

        private string GenerateRequestHash(int assignmentId, string recId)
        {
            // Combinar los parámetros en una cadena y luego generar un hash
            var rawData = $"{assignmentId}-{recId}";
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(rawData);
                var hashBytes = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }
    }
}
