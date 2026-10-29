using System.Diagnostics;
using System.Text.Json;
using Claims.Api.Services;
using Insurance.Contracts.Agent;
using Insurance.Helpers;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Authentication.Bff.Api.Agent
{
    public sealed class AgentChatService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _http;
        private readonly AgentToolLogStore _toolLog;
        private readonly ClaimsService _claims;
        private readonly SqlCommandExecutor _sql;

        public AgentChatService(
            IConfiguration config,
            IHttpClientFactory http,
            AgentToolLogStore toolLog,
            ClaimsService claims,
            SqlCommandExecutor sql)
        {
            _config = config;
            _http = http;
            _toolLog = toolLog;
            _claims = claims;
            _sql = sql;
        }

        public async Task<AgentChatResponse> ChatAsync(AgentChatRequest request, string accessToken, CancellationToken cancellationToken)
        {
            var message = request.Message ?? "";
            if (_config.GetValue("Agent:UsePythonAgents", false))
                return await ForwardPythonAsync(message, accessToken, cancellationToken);

            var builder = Kernel.CreateBuilder();
            AddChatModel(builder);

            var kernel = builder.Build();

            kernel.ImportPluginFromObject(new ClaimsTools(_claims), "claims");
            kernel.ImportPluginFromObject(new PoliciesTools(_sql), "policies");

            var history = new ChatHistory(
                "You are the Insurance Platform assistant. " +
                "A CLAIM is a loss filed against a policy. A POLICY is a contract. " +
                "When the user wants to create/file a claim and you already have policyNumber, claimantName, and amount " +
                "(from this message or earlier turns), you MUST call the claims.CreateClaim tool. " +
                "Do not ask for confirmation. Do not ask claim vs policy if they already said claim. " +
                "Treat 'policy id POL-101' as policyNumber POL-101. Never invent URLs.");

            foreach (var turn in request.History ?? [])
            {
                if (string.IsNullOrWhiteSpace(turn.Text))
                    continue;
                if (turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
                    history.AddAssistantMessage(turn.Text);
                else
                    history.AddUserMessage(turn.Text);
            }

            history.AddUserMessage(message);

            var toolsCalled = new List<string>();
            kernel.FunctionInvocationFilters.Add(new ToolNameFilter(toolsCalled, _toolLog));

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

        /// <summary>
        /// Same idea as HeathCareAssitantBypk LLMClient: OpenAI-compatible chat + optional base URL.
        /// Set Agent:Llm:Provider to OpenAI | AzureOpenAI | Groq | OpenRouter.
        /// </summary>
        private void AddChatModel(IKernelBuilder builder)
        {
            var provider = (_config["Agent:Llm:Provider"]
                ?? (string.IsNullOrWhiteSpace(_config["Agent:AzureOpenAI:Endpoint"]) ? "OpenAI" : "AzureOpenAI"))
                .Trim();

            if (provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddAzureOpenAIChatCompletion(
                    _config["Agent:Llm:AzureDeployment"] ?? _config["Agent:AzureOpenAI:Deployment"] ?? "gpt-4o-mini",
                    _config["Agent:Llm:AzureEndpoint"] ?? _config["Agent:AzureOpenAI:Endpoint"] ?? "",
                    _config["Agent:Llm:ApiKey"] ?? _config["Agent:AzureOpenAI:ApiKey"] ?? "");
                return;
            }

            var apiKey = _config["Agent:Llm:ApiKey"] ?? _config["Agent:OpenAI:ApiKey"] ?? "";
            var model = _config["Agent:Llm:Model"] ?? _config["Agent:OpenAI:Model"] ?? "gpt-4o-mini";
            var baseUrl = (_config["Agent:Llm:BaseUrl"] ?? "").Trim().TrimEnd('/');

            if (provider.Equals("Groq", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(baseUrl))
                    baseUrl = "https://api.groq.com/openai/v1";
                if (model is "gpt-4o-mini" or "")
                    model = "llama-3.1-8b-instant";
            }
            else if (provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(baseUrl))
                    baseUrl = "https://openrouter.ai/api/v1";
            }

            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                builder.AddOpenAIChatCompletion(model, new Uri(baseUrl), apiKey);
                return;
            }

            builder.AddOpenAIChatCompletion(model, apiKey);
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
        private readonly AgentToolLogStore _log;

        public ToolNameFilter(List<string> names, AgentToolLogStore log)
        {
            _names = names;
            _log = log;
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            var name = $"{context.Function.PluginName}.{context.Function.Name}";
            _names.Add(name);

            var requestJson = SerializeArguments(context.Arguments);
            var sw = Stopwatch.StartNew();
            var succeeded = true;
            string? responseJson = null;
            string? details = null;
            try
            {
                await next(context);
                responseJson = SerializeResult(context.Result);
            }
            catch (Exception ex)
            {
                succeeded = false;
                details = ex.ToString();
                throw;
            }
            finally
            {
                sw.Stop();
                details = succeeded
                    ? $"plugin={context.Function.PluginName}; tool={context.Function.Name}; durationMs={sw.ElapsedMilliseconds}"
                    : details;
                await _log.WriteAsync(
                    context.Function.PluginName ?? "",
                    context.Function.Name,
                    requestJson,
                    responseJson,
                    details,
                    (int)sw.ElapsedMilliseconds,
                    succeeded);
            }
        }

        private static string SerializeArguments(KernelArguments args)
        {
            try
            {
                var map = args.ToDictionary(x => x.Key, x => x.Value);
                return JsonSerializer.Serialize(map);
            }
            catch
            {
                return args.ToString() ?? "";
            }
        }

        private static string? SerializeResult(FunctionResult? result)
        {
            if (result is null)
                return null;
            try
            {
                var value = result.GetValue<object>();
                return value is string s ? s : JsonSerializer.Serialize(value);
            }
            catch
            {
                return result.ToString();
            }
        }
    }
}
