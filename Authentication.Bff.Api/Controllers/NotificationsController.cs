using Insurance.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.Bff.Api.Controllers;

[ApiController]
[Route("auth/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly SqlAdapterNotificationStore _store;

    public NotificationsController(SqlAdapterNotificationStore store) => _store = store;

    [HttpGet]
    public IActionResult Recent([FromQuery] int take = 50) => Ok(_store.Recent(take));

    [HttpGet("unread")]
    public IActionResult Unread([FromQuery] int take = 50) => Ok(_store.Recent(take, unreadOnly: true));

    [HttpPost("{id:int}/read")]
    public IActionResult Read(int id) => _store.MarkRead(id) == 0 ? NotFound() : Ok();

    [HttpPost("{id:int}/delete")]
    public IActionResult Delete(int id) => _store.SoftDelete(id) == 0 ? NotFound() : Ok();
}
