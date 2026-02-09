using ITSM_WS.Data.Models;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ITSM_WS.Services
{
    public class ScheduleService
    {
        private readonly IdempotencyService _idempotencyService;
        private readonly ExeconService _execonService;

        public ScheduleService(IdempotencyService idempotencyService, ExeconService execonService)
        {
            _idempotencyService = idempotencyService;
            _execonService = execonService;
        }

        public async Task<string> ProcessRequest(int assignmentId, string recId)
        {
            const string endpoint = "CallExternalApi";
            var hash = GenerateRequestHash(assignmentId, recId);

            //var existing = await _idempotencyService.GetAsync(recId, endpoint);
            Trace.TraceInformation("Antes await");
            var request = await _idempotencyService.StartOrGetAsync(recId, endpoint, hash)
                .ConfigureAwait(false);
            Trace.TraceInformation("Después await");

            if (request.RequestHash != hash)
            {
                await _idempotencyService.MarkDeadAsync(request, "Idempotency-Key reused with different payload")
                    .ConfigureAwait(false);
                return "Solicitud inválida: Idempotency-Key reutilizado con datos distintos.";
            }

            #region 🚫 Estados bloqueantes
            // 1️⃣ Ya completado
            if (request.Status == IdempotencyStatus.Completed)
                return request.Response ?? "Solicitud ya procesada.";
            // 2️⃣ InProgress activo
            if (request.Status == IdempotencyStatus.InProgress)
            {
                var timeout = int.Parse(ConfigurationManager.AppSettings["Idempotency.InProgressTimeoutMinutes"]);

                if (!_idempotencyService.IsInProgressExpired(request, timeout))
                    return "Solicitud en proceso. Intenta más tarde.";

                // Intentar takeover
                var taken = await _idempotencyService.TryTakeoverAsync(request)
                    .ConfigureAwait(false);
                if (!taken)
                    return "Otro proceso retomó la solicitud.";
            }

            // 3️⃣ Failed → retry
            if (request.Status == IdempotencyStatus.Failed)
            {
                var maxAttempts = int.Parse(ConfigurationManager.AppSettings["Idempotency.MaxAttempts"]);

                if (!_idempotencyService.CanRetry(request, maxAttempts))
                {
                    await _idempotencyService.MarkDeadAsync(request, "Max retries exceeded")
                        .ConfigureAwait(false);
                    return "Solicitud marcada como Dead.";
                }

                if (!_idempotencyService.BackoffElapsed(request))
                    return "Backoff activo. Reintenta más tarde.";
            }
            #endregion

            bool success = false;
            string responseMessage;
            var idempotencyKey = $"{assignmentId}:{recId}";
            var lockAcquired = await _idempotencyService.TryAcquireLockAsync(request)
                .ConfigureAwait(false);
            if (!lockAcquired)
            {
                return "Solicitud ya está siendo procesada por otro proceso.";
            }

            try
            {
                // Procesar la tarea con ExeconService
                success = await _execonService.ScheduleTaskAsync(assignmentId, idempotencyKey)
                    .ConfigureAwait(false);
                responseMessage = success
                    ? $"Tarea {assignmentId} ejecutada correctamente."
                    : $"Error al ejecutar tarea {assignmentId}.";

                if (success)
                {
                    await _idempotencyService.CompleteAsync(request, true, responseMessage)
                        .ConfigureAwait(false);
                }
                else
                {
                    await _idempotencyService.MarkFailedAsync(request, responseMessage)
                        .ConfigureAwait(false);
                }

                return responseMessage;
            }
            catch (Exception ex)
            {
                success = false;
                await _idempotencyService.MarkFailedAsync(request, $"Excepción al ejecutar tarea: {ex.Message}")
                    .ConfigureAwait(false);

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