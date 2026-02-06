using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Web;

namespace ITSM_WS.Models
{
    [Table("IdempotencyEvents")]
    public class IdempotencyEvent
    {
        [Key]
        public int Id { get; set; }

        public string RecId { get; set; }
        public string Endpoint { get; set; }

        public IdempotencyStatus OldStatus { get; set; }
        public IdempotencyStatus NewStatus { get; set; }

        public int AttemptCount { get; set; }
        public string Message { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}