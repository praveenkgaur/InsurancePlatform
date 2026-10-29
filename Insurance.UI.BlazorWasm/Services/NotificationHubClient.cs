using Insurance.Contracts.Notifications;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http.Json;
using Insurance.Contracts.Auth;


namespace Insurance.UI.BlazorWasm.Services
{
    public class NotificationHubClient : IAsyncDisposable
    {
        private readonly IConfiguration _config;
        private HubConnection _hubConnection;
        private readonly HttpClient _http;

        public List<InsuranceNotificationDto> NotificationItems { get; } = [];
        public NotificationHubClient(IConfiguration config, HttpClient http) 
        {
            _config = config;
            _http = http;
        }
        public event Action<InsuranceNotificationDto> NotificationReceived;
        public async Task StartAsync()
        {
            if (_hubConnection is { State: HubConnectionState.Connected or HubConnectionState.Connecting })
                return;

            var url = _config["SignalR:HubUrl"] ?? "http://localhost:5281/hubs/notifications";
            var method = _config["SignalR:ClientMethodName"] ?? NotificationEvents.ClientMethod;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.HttpMessageHandlerFactory = inner => new CookieSendingHandler { InnerHandler = inner };

                    if (_config.GetValue("SignalR:UseAzure", false))
                    {
                        options.AccessTokenProvider = async () =>
                        {
                            var token = await _http.GetFromJsonAsync<SignalRTokenResponse>("auth/signalr-token");
                            return token?.AccessToken;
                        };
                    }
                }).WithAutomaticReconnect()
            .Build();

            // Register BEFORE StartAsync so an early message is not missed.

            _hubConnection.On<InsuranceNotificationDto>(method, dto =>
            {
                NotificationItems.Insert(0, dto);
                NotificationReceived?.Invoke(dto);
            });

            try
            {
                await _hubConnection.StartAsync();
            }
            catch (HttpRequestException)
            {
                // No session cookie yet (login page). Hub requires authorization.
            }
            catch (Exception)
            {
                // Negotiate 401 / policy block — do not crash the login UI.
            }
        }

        public async ValueTask DisposeAsync()
        {
             if(_hubConnection is not null)
                await _hubConnection.DisposeAsync();   
        }
        private sealed class CookieSendingHandler : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}

