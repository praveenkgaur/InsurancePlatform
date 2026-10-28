using System.Net;
using System.Net.Http.Json;
using Insurance.Contracts.Notifications;

namespace Insurance.UI.BlazorWasm.Services;

public sealed class NotificationApi
{
    private readonly HttpClient _http;
    public NotificationApi(HttpClient http) => _http = http;

    public Task<InsuranceNotificationDto[]> GetAsync() => GetList("auth/notifications");

    public Task<InsuranceNotificationDto[]> GetUnreadAsync() => GetList("auth/notifications/unread");

    public async Task<bool> MarkReadAsync(int id)
    {
        var response = await _http.PostAsync($"auth/notifications/{id}/read", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SoftDeleteAsync(int id)
    {
        var response = await _http.PostAsync($"auth/notifications/{id}/delete", null);
        return response.IsSuccessStatusCode;
    }

    private async Task<InsuranceNotificationDto[]> GetList(string url)
    {
        var response = await _http.GetAsync(url);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return [];
        if (!response.IsSuccessStatusCode)
            return [];
        return await response.Content.ReadFromJsonAsync<InsuranceNotificationDto[]>() ?? [];
    }
}
