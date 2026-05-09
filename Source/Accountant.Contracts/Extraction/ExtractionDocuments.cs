namespace Accountant.Contracts;

public sealed record Document
{
    public string? Number { get; init; }
    public string? Date { get; init; }
    public string? TaxEventDate { get; init; }
    public string? DueDate { get; init; }
    public string? Currency { get; init; }
    public string? ExchangeRate { get; init; }
    public string? Place { get; init; }
    public string? Notes { get; init; }
}

public sealed record Party
{
    public string? Name { get; init; }
    public string? Eik { get; init; }
    public string? VatNumber { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; } = "BG";
    public string? Mol { get; init; }
}

public sealed record Totals
{
    public string? Net { get; init; }
    public string? Vat { get; init; }
    public string? Gross { get; init; }
    public string? Discount { get; init; }
    public string? Rounding { get; init; }
    public string? AmountDue { get; init; }
}

public sealed record VatBreakdownItem
{
    public string? Rate { get; init; }
    public string? Net { get; init; }
    public string? Vat { get; init; }
    public string? Gross { get; init; }
    public string? Reason { get; init; }
}

public sealed record Payment
{
    public PaymentMethod? Method { get; init; }
    public string? Amount { get; init; }
    public string? Currency { get; init; }
    public string? Iban { get; init; }
    public string? Bic { get; init; }
    public string? BankName { get; init; }
}

public sealed record LineItem
{
    public string? Description { get; init; }
    public string? Quantity { get; init; }
    public string? Unit { get; init; }
    public string? UnitPrice { get; init; }
    public string? DiscountPct { get; init; }
    public string? VatRate { get; init; }
    public string? Net { get; init; }
    public string? Vat { get; init; }
    public string? Gross { get; init; }
    public bool? IncludesVat { get; init; }
}

public sealed record Fiscal
{
    public string? FiscalReceiptNumber { get; init; }
    public string? FiscalDeviceNumber { get; init; }
    public string? Operator { get; init; }
    public string? QrCode { get; init; }
}

public sealed record Extraction
{
    public DocumentType DocumentType { get; init; } = DocumentType.Unknown;
    public string? Language { get; init; } = "bg";
    public string? Country { get; init; } = "BG";
    public Document Document { get; init; } = new();
    public Party Supplier { get; init; } = new();
    public Party Customer { get; init; } = new();
    public Totals Totals { get; init; } = new();
    public IReadOnlyList<VatBreakdownItem> VatBreakdown { get; init; } = [];
    public IReadOnlyList<Payment> Payments { get; init; } = [];
    public IReadOnlyList<LineItem> LineItems { get; init; } = [];
    public Fiscal Fiscal { get; init; } = new();
}
