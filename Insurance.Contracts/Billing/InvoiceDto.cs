namespace Insurance.Contracts.Billing;

public sealed class InvoiceDto
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = "";
    public string PolicyNumber { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public DateTime DueDate { get; set; }
}
