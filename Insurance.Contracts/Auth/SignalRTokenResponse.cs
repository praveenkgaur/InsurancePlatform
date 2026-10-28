using System;
using System.Collections.Generic;
using System.Text;

namespace Insurance.Contracts.Auth
{
    public sealed class SignalRTokenResponse
    {
        public string AccessToken { get; set; } = "";
        public int ExpiresInSeconds { get; set; }
    }
}
