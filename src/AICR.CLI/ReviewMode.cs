namespace AICR.CLI;

/// <summary>
/// Режимы работы код-ревью
/// </summary>
public enum ReviewMode
{
    /// <summary>
    /// Interactive mode (user selects mode)
    /// </summary>
    Interactive,

    /// <summary>
    /// Pre-commit review (staged files)
    /// </summary>
    PreCommit,

    /// <summary>
    /// Branch review (current vs base branch)
    /// </summary>
    Branch,

    /// <summary>
    /// Remote review (colleague's branch)
    /// </summary>
    Remote,

    /// <summary>
    /// Custom refs (manual base and head)
    /// </summary>
    Custom
}
