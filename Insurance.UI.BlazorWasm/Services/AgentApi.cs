using Insurance.Contracts.Agent;
using System.Net.Http.Json;

namespace Insurance.UI.BlazorWasm.Services
{
    public sealed class AgentApi
    {
        private readonly HttpClient _http;
        public AgentApi(HttpClient http) => _http = http;

        public async Task<AgentMetaResponse> MetaAsync()
        {
            var response = await _http.GetAsync("api/agent/meta");
            if (!response.IsSuccessStatusCode)
                return new AgentMetaResponse { Mode = "Single" };
            return await response.Content.ReadFromJsonAsync<AgentMetaResponse>()
                ?? new AgentMetaResponse();
        }

        public async Task<AgentChatResponse> ChatAsync(string message, IReadOnlyList<AgentChatTurn>? history = null, CaseBoardDto? caseBoard = null)
        {
            var response = await _http.PostAsJsonAsync("api/agent/chat", new AgentChatRequest
            {
                Message = message,
                History = history?.ToList() ?? [],
                CaseBoard = caseBoard
            });

            if (!response.IsSuccessStatusCode)
                return new AgentChatResponse { Reply = $"HTTP {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}" };

            return await response.Content.ReadFromJsonAsync<AgentChatResponse>()
                ?? new AgentChatResponse { Reply = "" };
        }
    }
}