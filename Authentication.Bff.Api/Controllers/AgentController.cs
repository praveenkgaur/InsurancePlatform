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

            var result = await _agent.ChatAsync(request.Message, accessToken, cancellationToken);
            return Ok(result);
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
