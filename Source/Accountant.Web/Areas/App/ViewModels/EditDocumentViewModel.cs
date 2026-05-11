using System.ComponentModel.DataAnnotations;
using Accountant.Contracts;

namespace Accountant.Web.Areas.App.ViewModels;

public sealed class EditDocumentViewModel
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public bool IsImage { get; set; }

    public DocumentType DocumentType { get; set; } = DocumentType.Invoice;

    // ---- Document --------------------------------------------------------

    [StringLength(120)]
    public string? Number { get; set; }

    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Очаквам формат YYYY-MM-DD.")]
    public string? Date { get; set; }

    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Очаквам формат YYYY-MM-DD.")]
    public string? TaxEventDate { get; set; }

    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "Очаквам формат YYYY-MM-DD.")]
    public string? DueDate { get; set; }

    [StringLength(3, MinimumLength = 3, ErrorMessage = "ISO 4217 код (3 букви).")]
    public string? Currency { get; set; }

    [Amount]
    public string? ExchangeRate { get; set; }

    [StringLength(200)]
    public string? Place { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    // ---- Parties ---------------------------------------------------------

    public PartyForm Supplier { get; set; } = new();
    public PartyForm Customer { get; set; } = new();

    // ---- Totals ----------------------------------------------------------

    [Amount] public string? TotalsNet { get; set; }
    [Amount] public string? TotalsVat { get; set; }
    [Amount] public string? TotalsGross { get; set; }
    [Amount] public string? TotalsDiscount { get; set; }
    [Amount] public string? TotalsRounding { get; set; }
    [Amount] public string? TotalsAmountDue { get; set; }

    // ---- Line items + payment + fiscal ----------------------------------

    public List<LineItemForm> Lines { get; set; } = new();

    public PaymentForm Payment { get; set; } = new();

    public FiscalForm Fiscal { get; set; } = new();
}

public sealed class PartyForm
{
    [StringLength(300)]
    public string? Name { get; set; }

    [RegularExpression(@"^(\d{9}|\d{13})$", ErrorMessage = "ЕИК трябва да е 9 или 13 цифри.")]
    public string? Eik { get; set; }

    [RegularExpression(@"^BG\d{9,10}$", ErrorMessage = "ДДС № трябва да започва с BG и да е 11-12 символа.")]
    public string? VatNumber { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(2)]
    public string? Country { get; set; }

    [StringLength(300)]
    public string? Mol { get; set; }
}

public sealed class LineItemForm
{
    [StringLength(1000)]
    public string? Description { get; set; }

    [Amount] public string? Quantity { get; set; }

    [StringLength(20)]
    public string? Unit { get; set; }

    [Amount] public string? UnitPrice { get; set; }
    [Amount] public string? DiscountPct { get; set; }
    [Amount] public string? VatRate { get; set; }
    [Amount] public string? Net { get; set; }
    [Amount] public string? Vat { get; set; }
    [Amount] public string? Gross { get; set; }
    public bool? IncludesVat { get; set; }
}

public sealed class PaymentForm
{
    public PaymentMethod? Method { get; set; }

    [Amount] public string? Amount { get; set; }

    [StringLength(3, MinimumLength = 3)]
    public string? Currency { get; set; }

    [RegularExpression(@"^[A-Z]{2}\d{2}[A-Z0-9]{1,30}$", ErrorMessage = "Невалиден IBAN.")]
    public string? Iban { get; set; }

    [RegularExpression(@"^[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$", ErrorMessage = "Невалиден BIC/SWIFT.")]
    public string? Bic { get; set; }

    [StringLength(200)]
    public string? BankName { get; set; }
}

public sealed class FiscalForm
{
    [StringLength(60)]
    public string? FiscalReceiptNumber { get; set; }

    [StringLength(60)]
    public string? FiscalDeviceNumber { get; set; }

    [StringLength(100)]
    public string? Operator { get; set; }

    [StringLength(500)]
    public string? QrCode { get; set; }
}

/// Decimal-like string, matches the v2 schema's `number-as-string` format
/// (preserves AI's formatting decisions).
public sealed class AmountAttribute : RegularExpressionAttribute
{
    public AmountAttribute()
        : base(@"^-?\d+(\.\d{1,4})?$")
    {
        ErrorMessage = "Очаквам число с до 4 знака след запетая (пример: 123.45).";
    }
}
