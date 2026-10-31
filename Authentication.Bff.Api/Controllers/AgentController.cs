using Authentication.Bff.Api.Agent;
using Authentication.Bff.Api.Services;
using Insurance.Contracts.Agent;
using Insurance.Contracts.Common;
using Insurance.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.Bff.Api.Controllers
{
    [Route("api/agent")]
    [ApiController]
    public class AgentController : ControllerBase
    {
        private readonly LoginService _login;
        private readonly AgentChatService _agent;
        private readonly IConfiguration _config;

        public AgentController(LoginService login, AgentChatService agent, IConfiguration config)
        {
            _login = login;
            _agent = agent;
            _config = config;
        }

        [HttpPost("chat")]
        public async Task<ActionResult<AgentChatResponse>> Chat([FromBody] AgentChatRequest request, CancellationToken cancellationToken)
        {
            if (!_config.GetValue("Agent:UseSemanticKernel", false) && !_config.GetValue("Agent:UsePythonAgents", false))
                return NotFound(new ApiMessage { Message = "Agent is off (Agent:UseSemanticKernel / UsePythonAgents)." });

            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest(new ApiMessage { Message = "Message is required." });

            if (!TryReadSession(out var sessionId, out var rawToken))
                return Unauthorized();

            var accessToken = await _login.GetAccessTokenAsync(sessionId, rawToken);
            if (string.IsNullOrWhiteSpace(accessToken))
                return Unauthorized();

            var result = await _agent.ChatAsync(request, accessToken, cancellationToken);
            return Ok(result);
        }

        [HttpGet("meta")]
        public ActionResult<AgentMetaResponse> Meta()
        {
            var mode = _config["Agent:Mode"] ?? "Single";
            return Ok(new AgentMetaResponse
            {
                Mode = mode,
                Orchestrator = _config["Agent:Orchestrator"] ?? "Coordinator",
                Intake = GuidedIntake.Start(),
                Agents =
                [
                    new AgentMetaItem
            {
                Key = "Claimant",
                DisplayName = _config["Agent:Agents:Claimant:DisplayName"] ?? "Claimant agent",
                Enabled = _config.GetValue("Agent:Agents:Claimant:Enabled", true)
            },
            new AgentMetaItem
            {
                Key = "Policy",
                DisplayName = _config["Agent:Agents:Policy:DisplayName"] ?? "Policy agent",
                Enabled = _config.GetValue("Agent:Agents:Policy:Enabled", true)
            },
            new AgentMetaItem
            {
                Key = "Claim",
                DisplayName = _config["Agent:Agents:Claim:DisplayName"] ?? "Claim agent",
                Enabled = _config.GetValue("Agent:Agents:Claim:Enabled", true)
            }
                ]
            });
        }
        private bool TryReadSession(out Guid sessionId, out string rawToken)
        {
            sessionId = Guid.Empty;
            rawToken = "";
            if (!Request.Cookies.TryGetValue(CookieSettings.Name(Request.IsHttps), out var value))
                return false;
            var parts = value.Split('.', 2);
            return parts.Length == 2 && Guid.TryParse(parts[0], out sessionId) && !string.IsNullOrWhiteSpace(rawToken = parts[1]);
        }

    }
}
