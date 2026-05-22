using AICR.Core.Interfaces;
using AICR.Core.Models;
using AICR.Core.Services;
using AICR.Providers.Abstractions;
using AICR.Providers.Gemini;
using AICR.Providers.Gemini.Models;
using Spectre.Console;

// Проверка аргументов
if (args.Length == 0)
{
    AnsiConsole.MarkupLine("[red]Usage:[/] dotnet run -- <repository-path> [base-ref] [head-ref]");
    AnsiConsole.MarkupLine("[dim]Examples:[/]");
    AnsiConsole.MarkupLine("  dotnet run -- /path/to/repo");
    AnsiConsole.MarkupLine("  dotnet run -- /path/to/repo main HEAD");
    AnsiConsole.MarkupLine("  dotnet run -- /path/to/repo HEAD~1 HEAD");
    return 1;
}

var repoPath = args[0];
var baseBranch = args.Length > 1 ? args[1] : "main";
var headBranch = args.Length > 2 ? args[2] : "HEAD";

// Инициализация сервисов
IGitService gitService = new GitService();
IReviewProvider reviewProvider = new GeminiCliProvider(new GeminiCliOptions());

// Проверка репозитория
if (!gitService.IsGitRepository(repoPath))
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {repoPath} is not a Git repository");
    return 1;
}

// Проверка доступности Gemini CLI
AnsiConsole.Status()
    .Start("Checking Gemini CLI availability...", ctx =>
    {
        var isAvailable = reviewProvider.IsAvailableAsync().Result;
        if (!isAvailable)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Gemini CLI is not available. Install it first.");
            Environment.Exit(1);
        }
        AnsiConsole.MarkupLine("[green]✓[/] Gemini CLI is available");
    });

// Получение текущей ветки
var currentBranch = await gitService.GetCurrentBranchAsync(repoPath);
AnsiConsole.MarkupLine($"[blue]Current branch:[/] {currentBranch}");
AnsiConsole.MarkupLine($"[blue]Comparing:[/] {baseBranch}...{headBranch}");

// Получение diff
DiffResult? diffResult = null;
try
{
    await AnsiConsole.Status()
        .StartAsync("Extracting git diff...", async ctx =>
        {
            diffResult = await gitService.GetDiffAsync(repoPath, baseBranch, headBranch);
            AnsiConsole.MarkupLine($"[green]✓[/] Found {diffResult.Files.Count} changed files");
        });
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
    return 1;
}

if (diffResult == null || string.IsNullOrWhiteSpace(diffResult.FullDiff))
{
    AnsiConsole.MarkupLine($"[yellow]No changes found between {baseBranch} and {headBranch}[/]");
    return 0;
}

// Показать статистику
var table = new Table();
table.AddColumn("File");
table.AddColumn("Status");
table.AddColumn("+");
table.AddColumn("-");

foreach (var file in diffResult.Files)
{
    table.AddRow(
        file.FilePath,
        file.Status,
        file.LinesAdded.ToString(),
        file.LinesRemoved.ToString()
    );
}

AnsiConsole.Write(table);

// Выполнить ревью
ReviewResult? reviewResult = null;
await AnsiConsole.Status()
    .StartAsync("Running AI code review...", async ctx =>
    {
        reviewResult = await reviewProvider.ReviewAsync(diffResult.FullDiff);
    });

if (reviewResult == null)
{
    AnsiConsole.MarkupLine("[red]Error:[/] Review failed");
    return 1;
}

if (!string.IsNullOrWhiteSpace(reviewResult.Error))
{
    var escapedError = Markup.Escape(reviewResult.Error);
    AnsiConsole.MarkupLine($"[red]Error:[/] {escapedError}");
    return 1;
}

// Показать результаты
AnsiConsole.WriteLine();
AnsiConsole.Write(new Rule($"[yellow]Review Results ({reviewResult.Issues.Count} issues found)[/]"));
AnsiConsole.WriteLine();

if (reviewResult.Issues.Count == 0)
{
    AnsiConsole.MarkupLine("[green]✓ No issues found! Code looks good.[/]");
}
else
{
    foreach (var issue in reviewResult.Issues)
    {
        var severityColor = issue.Severity switch
        {
            "error" => "red",
            "warning" => "yellow",
            _ => "blue"
        };

        var escapedMessage = Markup.Escape(issue.Message);
        var escapedFilePath = Markup.Escape(issue.FilePath ?? "General");
        var escapedCategory = Markup.Escape(issue.Category);

        var panel = new Panel($"[{severityColor}]{issue.Severity.ToUpper()}[/] {escapedCategory}\n\n{escapedMessage}")
        {
            Header = new PanelHeader(escapedFilePath),
            Border = BoxBorder.Rounded
        };

        AnsiConsole.Write(panel);

        if (!string.IsNullOrWhiteSpace(issue.Suggestion))
        {
            var escapedSuggestion = Markup.Escape(issue.Suggestion);
            AnsiConsole.MarkupLine($"[dim]💡 Suggestion: {escapedSuggestion}[/]");
        }

        AnsiConsole.WriteLine();
    }
}

AnsiConsole.MarkupLine($"[dim]Completed in {reviewResult.Duration.TotalSeconds:F2}s using {reviewResult.ModelName}[/]");

return 0;