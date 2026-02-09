using System;
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
            int timeoutSeconds = Int32.Parse(ConfigurationManager.AppSettings["HttpClient.TimeoutSeconds"]);
            _client = new HttpClient
            {
                BaseAddress = new System.Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            _ambiente = ambiente;
        }
        
        public async Task<bool> ScheduleTaskAsync(int assignmentId, string idempotencyKey)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_ambiente}/api/Execon/ScheduledTask/{assignmentId}"))
            {
                request.Headers.Add("Idempotency-Key", idempotencyKey);

                var response = await _client.SendAsync(request);

                var res = await response.Content.ReadAsStringAsync();

                return response.IsSuccessStatusCode;
            }
        }
    }
}