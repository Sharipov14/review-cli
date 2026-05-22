namespace AICR.Providers.Abstractions;

public interface IReviewProvider
{
    /// <summary>
    /// Название провайдера
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Проверка доступности провайдера
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Выполнить код-ревью
    /// </summary>
    /// <param name="diff">Git diff для анализа</param>
    /// <param name="customRules">Дополнительные правила проекта (опционально)</param>
    /// <param name="cancellationToken">Токен отмены</param>
    /// <returns>Результат ревью</returns>
    Task<ReviewResult> ReviewAsync(string diff, string? customRules = null, CancellationToken ct = default);
}

public record ReviewResult(
    string ProviderName,
    string ModelName,
    TimeSpan Duration,
    List<ReviewIssue> Issues,
    string RawResponse,
    string? Error = null
);

public record ReviewIssue(
    string Severity,      // "error", "warning", "info"
    string Category,      // "security", "performance", "style", "logic"
    string Message,
    string? FilePath = null,
    int? LineNumber = null,
    string? Suggestion = null
);
