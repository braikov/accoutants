namespace Accountant.Contracts;

public enum DocumentType
{
    Invoice,
    Receipt,
    Proforma,
    Protocol,
    CreditNote,
    DebitNote,
    Unknown,
}

public enum PaymentMethod
{
    Cash,
    Card,
    BankTransfer,
    Mixed,
    Unknown,
}

public enum Readability
{
    Excellent,
    Good,
    Fair,
    Poor,
    Unreadable,
}

public enum Engine
{
    Anthropic,
    OpenAi,
    Google,
    Other,
}

public enum Pipeline
{
    VisionDirect,
    OcrThenLlm,
    Hybrid,
    Manual,
    Other,
}

public enum CheckStatus
{
    Pass,
    Warning,
    Fail,
    Skipped,
}
