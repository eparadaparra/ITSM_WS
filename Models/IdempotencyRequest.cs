using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ITSM_WS.Models
{

    [Table("IdempotencyRequests")]
    public class IdempotencyRequest
    {
        [Key]
        public string RecId { get; set; }
        public string IdempotencyKey { get; set; }
        public string Endpoint { get; set; }
        public string RequestHash { get; set; }
        public IdempotencyStatus Status { get; set; }

        public int AttemptCount { get; set; }
        public DateTime? LastAttemptAt { get; set; }

        public string Response { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        [Timestamp]
        public byte[] RowVersion { get; set; }
    }

    public enum IdempotencyStatus
    {
        Pending     = 0, // Creado pero no tomado
        InProgress  = 1, // Lock adquirido
        Completed   = 2, // OK
        Failed      = 3, // Falló pero puede reintentar
        Dead        = 4  // No reintentar jamás
    }

}