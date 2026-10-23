using Insurance.Contracts.Billing;
using Insurance.Contracts.Security;
using Insurance.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
public sealed class BillingController : ControllerBase
{
    private readonly SqlCommandExecutor _sql;

    public BillingController(SqlCommandExecutor sql) => _sql = sql;

    [HttpGet]
    [Authorize(Policy = PermissionKeys.BillingView)]
    public Task<IReadOnlyList<InvoiceDto>> Get() =>
        _sql.QueryAsync<InvoiceDto>(
            """
            SELECT InvoiceId, InvoiceNumber, PolicyNumber, Amount, Status, DueDate
            FROM dbo.Invoices
            ORDER BY InvoiceId DESC
            """);
}
