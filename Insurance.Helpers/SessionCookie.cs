using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace Insurance.Helpers
{
    public static class SessionCookie
    {
        // Cookie value is "{sessionId:N}.{rawToken}" — same as AppendSessionCookie.
        public static bool TryRead(HttpRequest request, out Guid sessionId, out string rawToken)
        {
            sessionId = Guid.Empty;
            rawToken = "";
            if (!request.Cookies.TryGetValue(CookieSettings.Name(request.IsHttps), out var value))
                return false;
            var parts = value.Split('.', 2);
            return parts.Length == 2
                && Guid.TryParse(parts[0], out sessionId)
                && !string.IsNullOrWhiteSpace(rawToken = parts[1]);
        }
    }
}
