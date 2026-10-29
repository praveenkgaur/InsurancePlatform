using Insurance.Contracts.Agent;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Authentication.Bff.Api.Agent
{
    public sealed class AgentChatService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _http;

        public AgentChatService(IConfiguration config, IHttpClientFactory http)
        {
            _config = config;
            _http = http;
        }

        public async Task<AgentChatResponse> ChatAsync(string message, string accessToken, CancellationToken cancellationToken)
        {
            if (_config.GetValue("Agent:UsePythonAgents", false))
                return await ForwardPythonAsync(message, accessToken, cancellationToken);

            var claimsBase = (_config["Downstream:Claims"] ?? "http://localhost:5283").TrimEnd('/');
            var policiesBase = (_config["Downstream:Policies"] ?? "http://localhost:5284").TrimEnd('/');

            var claimsClient = _http.CreateClient("AgentClaims");
            claimsClient.BaseAddress = new Uri(claimsBase + "/");

            var policiesClient = _http.CreateClient("AgentPolicies");
            policiesClient.BaseAddress = new Uri(policiesBase + "/");

            var builder = Kernel.CreateBuilder();
            var azureEndpoint = _config["Agent:AzureOpenAI:Endpoint"];

            if (!string.IsNullOrWhiteSpace(azureEndpoint))
            {
                builder.AddAzureOpenAIChatCompletion(
                    _config["Agent:AzureOpenAI:Deployment"] ?? "gpt-4o-mini",
                    azureEndpoint,
                    _config["Agent:AzureOpenAI:ApiKey"] ?? "");
            }
            else
            {
                builder.AddOpenAIChatCompletion(
                    _config["Agent:OpenAI:Model"] ?? "gpt-4o-mini",
                    _config["Agent:OpenAI:ApiKey"] ?? "");
            }

            var kernel = builder.Build();

            kernel.ImportPluginFromObject(new ClaimsTools(claimsClient, accessToken), "claims");
            
            kernel.ImportPluginFromObject(new PoliciesTools(policiesClient, accessToken), "policies");

            var history = new ChatHistory(
                "You are the Insurance Platform assistant. " +
                "Use claims tools for losses/claimants. Use policies tools for policy contracts. " +
                "If the user is unclear, ask whether they mean a claim or a policy. " +
                "Never invent URLs.");

            history.AddUserMessage(message);

            var toolsCalled = new List<string>();
            kernel.FunctionInvocationFilters.Add(new ToolNameFilter(toolsCalled));

            var chat = kernel.GetRequiredService<IChatCompletionService>();
            var settings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };
            var result = await chat.GetChatMessageContentAsync(history, settings, kernel, cancellationToken);
            return new AgentChatResponse
            {
                Reply = result.Content ?? "",
                ToolsCalled = toolsCalled.ToArray()
            };
        }

        private async Task<AgentChatResponse> ForwardPythonAsync(string message, string accessToken, CancellationToken cancellationToken)
        {
            var python = (_config["Agent:PythonBaseUrl"] ?? "http://localhost:5290").TrimEnd('/');
            var client = _http.CreateClient("AgentPython");
            using var req = new HttpRequestMessage(HttpMethod.Post, python + "/chat")
            {
                Content = JsonContent.Create(new AgentChatRequest { Message = message })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            
            var response = await client.SendAsync(req, cancellationToken);
            
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return new AgentChatResponse { Reply = $"Python agent HTTP {(int)response.StatusCode}: {body}" };

            return System.Text.Json.JsonSerializer.Deserialize<AgentChatResponse>(body,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new AgentChatResponse { Reply = body };
        }
    }

    file sealed class ToolNameFilter : IFunctionInvocationFilter
    {
        private readonly List<string> _names;
        public ToolNameFilter(List<string> names) => _names = names;

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            _names.Add($"{context.Function.PluginName}-{context.Function.Name}");
            await next(context);
        }
    }
}
