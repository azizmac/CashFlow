using CashFlow.Domain.Shared;

namespace CashFlow.Domain.Ledger;

public enum TransactionStatus { Pending = 0, Posted = 1, Cancelled = 2 }

public enum TransactionKind
{
    Regular = 0,
    Transfer = 1,
    Fee = 2,
    Interest = 3,
    LoanPayment = 4,
    Investment = 5,
    Fx = 6,
    Cash = 7,
    Tax = 8,
}

public enum CategorySource { None = 0, Rule = 1, Mcc = 2, Ai = 3, User = 4, Counterparty = 5 }

/// <summary>
/// Операция. Поля от источника (сумма, дата, описание, контрагент) — неизменяемы после импорта.
/// Поля пользователя (категория, теги, заметка, TransferLink) — синк не перетирает.
/// </summary>
public sealed class Transaction : Entity
{
    private readonly List<string> _tags = [];

    private Transaction() { }

    public Transaction(
        Guid accountId,
        DateTimeOffset postedAt,
        Money amount,
        string description,
        ConnectorType source,
        DedupeKey dedupeKey,
        CounterpartyRaw? counterpartyRaw = null,
        string? purpose = null,
        string? mcc = null,
        TransactionStatus status = TransactionStatus.Posted,
        ExternalRef? externalRef = null,
        Guid? rawRecordId = null,
        DateTimeOffset? bookedAt = null)
    {
        AccountId = accountId;
        PostedAt = postedAt;
        BookedAt = bookedAt;
        Amount = amount;
        Description = description;
        Source = source;
        DedupeKey = dedupeKey;
        CounterpartyRaw = counterpartyRaw ?? CounterpartyRaw.Empty;
        Purpose = purpose;
        Mcc = mcc;
        Status = status;
        ExternalRef = externalRef;
        RawRecordId = rawRecordId;
        Kind = TransactionKind.Regular;
        Raise(new TransactionImported(Id, AccountId));
    }

    public Guid AccountId { get; private set; }
    public DateTimeOffset PostedAt { get; private set; }
    public DateTimeOffset? BookedAt { get; private set; }
    public Money Amount { get; private set; }
    /// <summary>Сумма в базовой валюте пользователя по курсу на дату. Кэш.</summary>
    public Money? AmountInBase { get; private set; }
    public TransactionStatus Status { get; private set; }
    public TransactionKind Kind { get; private set; }
    public string Description { get; private set; } = default!;
    /// <summary>Назначение платежа (для р/с).</summary>
    public string? Purpose { get; private set; }
    public string? Mcc { get; private set; }
    public ConnectorType Source { get; private set; }
    public ExternalRef? ExternalRef { get; private set; }
    public DedupeKey DedupeKey { get; private set; }
    public Guid? RawRecordId { get; private set; }

    public CounterpartyRaw CounterpartyRaw { get; private set; } = CounterpartyRaw.Empty;
    public Guid? CounterpartyId { get; private set; }

    public Guid? CategoryId { get; private set; }
    public CategorySource CategorySource { get; private set; }
    public decimal? CategoryConfidence { get; private set; }
    /// <summary>Предложение ИИ/правила с низкой уверенностью, ждущее подтверждения.</summary>
    public Guid? ProposedCategoryId { get; private set; }
    public bool ReviewedByUser { get; private set; }

    public Guid? TransferLinkId { get; private set; }
    public string? Note { get; private set; }
    public IReadOnlyList<string> Tags => _tags;

    public bool IsExpense => Amount.IsNegative;
    public bool IsIncome => Amount.IsPositive;
    public DateOnly PostedDate => DateOnly.FromDateTime(PostedAt.UtcDateTime);

    // --- Изменения от источника ---

    public void UpdateFromSource(TransactionStatus status, DateTimeOffset? bookedAt, string? mcc)
    {
        Status = status;
        BookedAt ??= bookedAt;
        Mcc ??= mcc;
        Touch();
    }

    /// <summary>
    /// Тот же документ пришёл из более полного источника (например, 1С после краткой XLSX): переписываем реквизиты контрагента,
    /// назначение и описание. Пользовательские поля (категория, заметка, теги, перевод) не трогаем.
    /// </summary>
    public bool EnrichFromSource(CounterpartyRaw counterparty, string? purpose, string description)
    {
        // Новый источник богаче, если принёс ИНН, которого не было, или настоящее назначение платежа вместо
        // служебного («ВО 17, док. 123» из краткой выписки), или имя контрагента там, где его не было.
        var hasRealPurpose = !string.IsNullOrWhiteSpace(purpose) && (string.IsNullOrWhiteSpace(Purpose) || Purpose.StartsWith("ВО ", StringComparison.Ordinal));
        var richer = counterparty.Inn is { Length: > 0 } && CounterpartyRaw.Inn is null
                     || hasRealPurpose
                     || (string.IsNullOrWhiteSpace(CounterpartyRaw.Name) && !string.IsNullOrWhiteSpace(counterparty.Name));
        if (!richer) return false;
        CounterpartyRaw = counterparty;
        Purpose = purpose ?? Purpose;
        Description = description;
        Touch();
        return true;
    }

    public void SetAmountInBase(Money amountInBase) { AmountInBase = amountInBase; Touch(); }
    public void SetKind(TransactionKind kind) { Kind = kind; Touch(); }

    public void ResolveCounterparty(Guid counterpartyId)
    {
        if (CounterpartyId == counterpartyId) return;
        CounterpartyId = counterpartyId;
        Touch();
        Raise(new CounterpartyResolved(Id, counterpartyId));
    }

    // --- Категоризация ---

    /// <summary>Автоматическая категоризация. Никогда не перетирает ручную.</summary>
    public bool Categorize(Guid categoryId, CategorySource source, decimal confidence, decimal acceptThreshold = 0.7m)
    {
        if (ReviewedByUser || CategorySource == CategorySource.User) return false;
        if (confidence >= acceptThreshold)
        {
            CategoryId = categoryId;
            CategorySource = source;
            CategoryConfidence = confidence;
            ProposedCategoryId = null;
        }
        else
        {
            ProposedCategoryId = categoryId;
        }
        Touch();
        Raise(new TransactionCategorized(Id, categoryId, source, confidence));
        return true;
    }

    public void SetCategoryByUser(Guid? categoryId)
    {
        var previous = CategoryId;
        CategoryId = categoryId;
        CategorySource = categoryId is null ? CategorySource.None : CategorySource.User;
        CategoryConfidence = 1m;
        ProposedCategoryId = null;
        ReviewedByUser = true;
        Touch();
        Raise(new TransactionRecategorized(Id, previous, categoryId, CounterpartyId, Description));
    }

    public void AcceptProposal()
    {
        if (ProposedCategoryId is null) return;
        SetCategoryByUser(ProposedCategoryId);
    }

    // --- Пользовательские поля ---

    public void SetNote(string? note) { Note = note; Touch(); }
    public void SetTags(IEnumerable<string> tags) { _tags.Clear(); _tags.AddRange(tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct()); Touch(); }

    public void LinkTransfer(Guid linkId)
    {
        TransferLinkId = linkId;
        Kind = TransactionKind.Transfer;
        Touch();
    }

    public void UnlinkTransfer()
    {
        TransferLinkId = null;
        if (Kind == TransactionKind.Transfer) Kind = TransactionKind.Regular;
        Touch();
    }
}

/// <summary>Связь двух операций: перевод между своими счетами. Исключается из доходов/расходов.</summary>
public sealed class TransferLink : Entity
{
    private TransferLink() { }

    public TransferLink(Guid outgoingTransactionId, Guid incomingTransactionId, bool auto)
    {
        OutgoingTransactionId = outgoingTransactionId;
        IncomingTransactionId = incomingTransactionId;
        IsAutomatic = auto;
    }

    public Guid OutgoingTransactionId { get; private set; }
    public Guid IncomingTransactionId { get; private set; }
    public bool IsAutomatic { get; private set; }
}

public sealed record TransactionImported(Guid TransactionId, Guid AccountId) : IDomainEvent
{ public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow; }

public sealed record CounterpartyResolved(Guid TransactionId, Guid CounterpartyId) : IDomainEvent
{ public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow; }

public sealed record TransactionCategorized(Guid TransactionId, Guid CategoryId, CategorySource Source, decimal Confidence) : IDomainEvent
{ public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow; }

public sealed record TransactionRecategorized(Guid TransactionId, Guid? PreviousCategoryId, Guid? NewCategoryId, Guid? CounterpartyId, string Description) : IDomainEvent
{ public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow; }

public sealed record TransferLinked(Guid LinkId, Guid OutgoingTransactionId, Guid IncomingTransactionId) : IDomainEvent
{ public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow; }
