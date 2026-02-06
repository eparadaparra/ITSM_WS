using System.Configuration;
using System.Net.Http;
using System.Threading.Tasks;

namespace ITSM_WS.Services
{
    public class ExeconService
    {
        private readonly HttpClient _client;
        private readonly string _ambiente;

        public ExeconService(string baseUrl, string ambiente)
        {
            _client = new HttpClient
            {
                BaseAddress = new System.Uri(baseUrl)
            };
            _ambiente = ambiente;
        }
        
        public async Task<bool> ScheduleTaskAsync(int assignmentId, string idempotencyKey)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_ambiente}/api/Execon/ScheduledTask/{assignmentId}"))
            {
                // Agregar el recId como header
                request.Headers.Add("Idempotency-Key", idempotencyKey);

                // Enviar la solicitud
                var response = await _client.SendAsync(request);

                return response.IsSuccessStatusCode;
            }
        }
    }
}