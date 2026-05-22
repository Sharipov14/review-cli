using AICR.Core.Models;

namespace AICR.Core.Interfaces;

public interface IGitService
{
    /// <summary>
    /// Получить diff между двумя ветками
    /// </summary>
    Task<DiffResult> GetDiffAsync(
        string repositoryPath,
        string baseBranch = "main",
        string headBranch = "HEAD",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Получить имя текущей ветки
    /// </summary>
    Task<string> GetCurrentBranchAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверить, является ли путь Git репозиторием
    /// </summary>
    bool IsGitRepository(string path);
}
