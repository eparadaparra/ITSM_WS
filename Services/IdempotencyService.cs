using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Threading.Tasks;
using ITSM_WS.Data.Data;
using ITSM_WS.Data.Models;

namespace ITSM_WS.Services
{
    public class IdempotencyService
    {
        private readonly IdempotencyDbContext _db;

        public IdempotencyService(IdempotencyDbContext db)
        {
            _db = db;
        }

        public async Task<IdempotencyRequest> StartOrGetAsync(string recId, string endpoint, string requestHash)
        {
            System.Diagnostics.Trace.TraceInformation($"StartOrGetAsync: {recId}, {endpoint}");
            var existing = await _db.IdempotencyRequests
                .FirstOrDefaultAsync(tbl => tbl.RecId == recId && tbl.Endpoint == endpoint)
                .ConfigureAwait(false);

            if (existing != null)
            {
                return existing;
            }

            try
            {
                var entity = new IdempotencyRequest
                {
                    RecId = recId,
                    IdempotencyKey = recId,
                    Endpoint = endpoint,
                    RequestHash = requestHash,
                    Status = IdempotencyStatus.Pending,
                    AttemptCount = 0,
                    LastAttemptAt = null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow 
                };

                _db.IdempotencyRequests.Add(entity);
                System.Diagnostics.Trace.TraceInformation($"Intentando SaveChangesAsync para {recId}");
                await _db.SaveChangesAsync()
                    .ConfigureAwait(false);
                System.Diagnostics.Trace.TraceInformation($"SaveChangesAsync exitoso para {recId}");

                return entity;
            }
            catch (DbUpdateException ex)
            {
                System.Diagnostics.Trace.TraceWarning($"DbUpdateException para {recId}: {ex.Message}");
                return await _db.IdempotencyRequests.FirstAsync(tbl => tbl.RecId == recId && tbl.Endpoint == endpoint).ConfigureAwait(false);
            }
        }

        public bool CanRetry(IdempotencyRequest entity, int maxAttempts)
        {
            return entity.AttemptCount < maxAttempts;
        }

        public bool BackoffElapsed(IdempotencyRequest entity)
        {
            if (!entity.LastAttemptAt.HasValue)
                return true;

            var secondsToWait = entity.AttemptCount * 30; // 30s, 60s, 90s...
            return DateTime.UtcNow >= entity.LastAttemptAt.Value.AddSeconds(secondsToWait);
        }

        public async Task<bool> TryAcquireLockAsync(IdempotencyRequest entity)
        {
            if (entity.Status != IdempotencyStatus.Pending && entity.Status != IdempotencyStatus.Failed)
                return false;

            entity.Status = IdempotencyStatus.InProgress;
            entity.AttemptCount++;
            entity.LastAttemptAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync().ConfigureAwait(false);
                return true; // 🎯 ESTE PROCESO GANÓ
            }
            catch (DbUpdateConcurrencyException)
            {
                return false; // ❌ otro proceso ganó
            }
        }

        public async Task MarkDeadAsync(IdempotencyRequest entity, string reason)
        {
            var old = entity.Status;

            entity.Status = IdempotencyStatus.Dead;
            entity.Response = reason;
            entity.UpdatedAt = DateTime.UtcNow;

            await LogEventAsync(entity, old, entity.Status, reason).ConfigureAwait(false);
            await _db.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task CompleteAsync(IdempotencyRequest entity, bool success, string response)
        {
            var old = entity.Status;

            entity.Status = success
                ? IdempotencyStatus.Completed
                : IdempotencyStatus.Failed;

            entity.Response = response;
            entity.UpdatedAt = DateTime.UtcNow;

            await LogEventAsync(entity, old, entity.Status, success ? "OK" : "Error en ejecución").ConfigureAwait(false);
            await _db.SaveChangesAsync().ConfigureAwait(false);
        }

        public bool IsInProgressExpired(IdempotencyRequest entity, int timeoutMinutes)
        {
            if (entity.UpdatedAt == null)
                return false;

            return DateTime.UtcNow > entity.UpdatedAt.Value.AddMinutes(timeoutMinutes);
        }

        public async Task<bool> TryTakeoverAsync(IdempotencyRequest entity)
        {
            if (entity.Status != IdempotencyStatus.InProgress)
                return false;

            var old = entity.Status;

            entity.Status = IdempotencyStatus.InProgress;
            entity.UpdatedAt = DateTime.UtcNow;

            try
            {
                await LogEventAsync(entity, old, IdempotencyStatus.InProgress, "Takeover por timeout: lock reclamado por nuevo proceso").ConfigureAwait(false);
                await _db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Otro proceso ganó el takeover
                return false;
            }
        }

        public async Task MarkFailedAsync(IdempotencyRequest entity, string error)
        {
            if (entity.Status == IdempotencyStatus.Dead)
                return;

            var oldStatus = entity.Status;

            entity.Status = IdempotencyStatus.Failed;
            entity.AttemptCount++;
            entity.LastAttemptAt = DateTime.UtcNow;
            entity.Response = error;
            entity.UpdatedAt = DateTime.UtcNow;

            await LogEventAsync(entity, oldStatus, IdempotencyStatus.Failed, error).ConfigureAwait(false);

            await _db.SaveChangesAsync().ConfigureAwait(false);
        }
                       
        private Task LogEventAsync(IdempotencyRequest entity, IdempotencyStatus oldStatus, IdempotencyStatus newStatus, string message)
        {
            _db.IdempotencyEvents.Add(new IdempotencyEvent
            {
                RecId = entity.RecId,
                Endpoint = entity.Endpoint,
                OldStatus = oldStatus,
                NewStatus = newStatus,
                AttemptCount = entity.AttemptCount,
                Message = message,
                CreatedAt = DateTime.UtcNow
            });

            return Task.CompletedTask;
        }

    }
}