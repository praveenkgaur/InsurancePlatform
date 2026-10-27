using Insurance.Contracts.Notifications;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.AspNetCore.SignalR.Client;

namespace Insurance.UI.BlazorWasm.Services
{
    public class NotificationHubClient : IAsyncDisposable
    {
        private readonly IConfiguration _config;
        private HubConnection _hubConnection;

        public List<InsuranceNotificationDto> NotificationItems { get; } = [];
        public NotificationHubClient(IConfiguration config) => _config = config;
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
                    //options.HttpMessageHandlerFactory = inner =>   new CookieSendingHandler { InnerHandler = inner };
                }).WithAutomaticReconnect()
            .Build();

            // Register BEFORE StartAsync so an early message is not missed.

            _hubConnection.On<InsuranceNotificationDto>(method, dto =>
            {
                NotificationItems.Insert(0, dto);
                NotificationReceived?.Invoke(dto);
            });

            await _hubConnection.StartAsync();

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

