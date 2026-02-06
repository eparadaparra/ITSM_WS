using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Threading.Tasks;
using ITSM_WS.Data;
using ITSM_WS.Models;

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
            try
            {
                var entity = new IdempotencyRequest
                {
                    RecId          = recId,
                    IdempotencyKey = recId,
                    Endpoint       = endpoint,
                    RequestHash    = requestHash,
                    Status         = IdempotencyStatus.Pending,
                    AttemptCount   = 0,
                    LastAttemptAt  = null,
                    CreatedAt      = DateTime.UtcNow
                };

                _db.IdempotencyRequests.Add(entity);
                await _db.SaveChangesAsync();

                return entity;
            }
            catch (DbUpdateException)
            {
                // ⚠️ Ya existe → recuperar
                return await _db.IdempotencyRequests.FirstAsync(tbl => tbl.RecId == recId && tbl.Endpoint == endpoint);
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
                await _db.SaveChangesAsync();
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

            await LogEventAsync(entity, old, entity.Status, reason);
            await _db.SaveChangesAsync();
        }

        public async Task CompleteAsync(IdempotencyRequest entity, bool success, string response)
        {
            var old = entity.Status;

            entity.Status = success
                ? IdempotencyStatus.Completed
                : IdempotencyStatus.Failed;

            entity.Response = response;
            entity.UpdatedAt = DateTime.UtcNow;

            await LogEventAsync(entity, old, entity.Status, success ? "OK" : "Error en ejecución");
            await _db.SaveChangesAsync();
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

            // Reclamamos el lock (mantenemos InProgress)
            entity.Status = IdempotencyStatus.InProgress;
            entity.UpdatedAt = DateTime.UtcNow;

            try
            {
                await LogEventAsync(entity, old, IdempotencyStatus.InProgress, "Takeover por timeout: lock reclamado por nuevo proceso");
                await _db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Otro proceso ganó el takeover
                return false;
            }
        }

        private async Task LogEventAsync(IdempotencyRequest entity, IdempotencyStatus oldStatus, IdempotencyStatus newStatus, string message)
        {
            _db.IdempotencyEvents.Add(new IdempotencyEvent
            {
                RecId        = entity.RecId,
                Endpoint     = entity.Endpoint,
                OldStatus    = oldStatus,
                NewStatus    = newStatus,
                AttemptCount = entity.AttemptCount,
                Message      = message,
                CreatedAt    = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
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

            await LogEventAsync(entity, oldStatus, IdempotencyStatus.Failed, error);

            await _db.SaveChangesAsync();
        }


    }
}