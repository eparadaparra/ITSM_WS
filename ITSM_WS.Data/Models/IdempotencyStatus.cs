namespace ITSM_WS.Data.Models
{
    public enum IdempotencyStatus
    {
        Pending     = 0, // Creado pero no tomado
        InProgress  = 1, // Lock adquirido
        Completed   = 2, // OK
        Failed      = 3, // Falló pero puede reintentar
        Dead        = 4  // No reintentar jamás
    }
}