using AICR.Core.Interfaces;
using AICR.Core.Models;
using AICR.Core.Services;
using AICR.Providers.Abstractions;
using AICR.Providers.Gemini;
using AICR.Providers.Gemini.Models;
using Spectre.Console;

namespace AICR.CLI;

public class Program
{
    private static IGitService? _gitService;
    private static IReviewProvider? _reviewProvider;
    private static string _repoPath = string.Empty;

    public static async Task<int> Main(string[] args)
    {
        // Парсинг аргументов
        var options = ParseArguments(args);

        // Если запущено без аргументов или с --help
        if (options.ShowHelp)
        {
            ShowHelp();
            return 0;
        }

        _repoPath = options.RepoPath;
        await InitializeServices();

        // Если никакие флаги не указаны → интерактивный режим
        if (options.Mode == ReviewMode.Interactive)
        {
            await RunInteractiveMode();
            return 0;
        }

        // Иначе используем флаги
        switch (options.Mode)
        {
            case ReviewMode.PreCommit:
                await RunPreCommitReview();
                break;
            case ReviewMode.Branch:
                await RunBranchReview(options.BaseBranch ?? "main");
                break;
            case ReviewMode.Remote:
                await RunRemoteReview(options.RemoteBranch!);
                break;
            case ReviewMode.Custom:
                await RunCustomReview(options.BaseBranch ?? "main", options.HeadBranch ?? "HEAD");
                break;
        }

        return 0;
    }

    private static CommandLineOptions ParseArguments(string[] args)
    {
        var options = new CommandLineOptions
        {
            RepoPath = Directory.GetCurrentDirectory()
        };

        if (args.Length == 0)
        {
            options.Mode = ReviewMode.Interactive;
            return options;
        }

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    options.ShowHelp = true;
                    return options;

                case "--repo" or "-r":
                    if (i + 1 < args.Length)
                        options.RepoPath = args[++i];
                    break;

                case "--staged" or "-s":
                    options.Mode = ReviewMode.PreCommit;
                    break;

                case "--branch" or "-b":
                    options.Mode = ReviewMode.Branch;
                    break;

                case "--remote" or "-m":
                    options.Mode = ReviewMode.Remote;
                    if (i + 1 < args.Length)
                        options.RemoteBranch = args[++i];
                    break;

                case "--base":
                    if (i + 1 < args.Length)
                    {
                        options.BaseBranch = args[++i];
                        if (options.Mode == ReviewMode.Interactive)
                            options.Mode = ReviewMode.Custom;
                    }
                    break;

                case "--head":
                    if (i + 1 < args.Length)
                    {
                        options.HeadBranch = args[++i];
                        if (options.Mode == ReviewMode.Interactive)
                            options.Mode = ReviewMode.Custom;
                    }
                    break;

                case "--output" or "-o":
                    if (i + 1 < args.Length)
                        options.OutputFile = args[++i];
                    break;

                default:
                    // Поддержка старого синтаксиса: aicr <repo> [base] [head]
                    if (!args[i].StartsWith("-"))
                    {
                        if (string.IsNullOrEmpty(options.RepoPath) || options.RepoPath == Directory.GetCurrentDirectory())
                        {
                            options.RepoPath = args[i];
                        }
                        else if (string.IsNullOrEmpty(options.BaseBranch))
                        {
                            options.BaseBranch = args[i];
                            options.Mode = ReviewMode.Custom;
                        }
                        else if (string.IsNullOrEmpty(options.HeadBranch))
                        {
                            options.HeadBranch = args[i];
                            options.Mode = ReviewMode.Custom;
                        }
                    }
                    break;
            }
        }

        return options;
    }

    private static void ShowHelp()
    {
        AnsiConsole.Write(new FigletText("AICR CLI").Color(Color.DarkSeaGreen1_1));
        AnsiConsole.MarkupLine("[dim]AI Code Review - Автоматический код-ревью с использованием AI[/]\n");

        AnsiConsole.MarkupLine("[yellow]Использование:[/]");
        AnsiConsole.WriteLine("  aicr [options]                           # Интерактивный режим");
        AnsiConsole.WriteLine("  aicr --staged                            # Pre-commit review");
        AnsiConsole.WriteLine("  aicr --branch                            # Branch review (vs main)");
        AnsiConsole.WriteLine("  aicr --remote <branch>                   # Remote branch review");
        AnsiConsole.WriteLine("  aicr --base <ref> --head <ref>           # Custom refs");
        AnsiConsole.WriteLine("  aicr <repo> [base] [head]                # Старый синтаксис");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Опции:[/]");
        AnsiConsole.WriteLine("  -r, --repo <path>      Путь к Git репозиторию (по умолчанию: текущая директория)");
        AnsiConsole.WriteLine("  -s, --staged           Проверить staged файлы (pre-commit режим)");
        AnsiConsole.WriteLine("  -b, --branch           Проверить текущую ветку vs main");
        AnsiConsole.WriteLine("  -m, --remote <branch>  Проверить удалённую ветку");
        AnsiConsole.WriteLine("  --base <ref>           Base ref для сравнения");
        AnsiConsole.WriteLine("  --head <ref>           Head ref для сравнения");
        AnsiConsole.WriteLine("  -o, --output <file>    Сохранить отчёт в файл");
        AnsiConsole.WriteLine("  -h, --help             Показать эту справку");
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[yellow]Примеры:[/]");
        AnsiConsole.WriteLine("  aicr                                     # Интерактивный выбор режима");
        AnsiConsole.WriteLine("  aicr --staged                            # Проверить staged изменения");
        AnsiConsole.WriteLine("  aicr --branch                            # Сравнить с main");
        AnsiConsole.WriteLine("  aicr --base HEAD~1 --head HEAD           # Последний коммит");
        AnsiConsole.WriteLine("  aicr /path/to/repo main feature          # Старый синтаксис");
    }

    private static async Task InitializeServices()
    {
        _gitService = new GitService();
        _reviewProvider = new GeminiCliProvider(new GeminiCliOptions());

        // Проверка репозитория
        if (!_gitService.IsGitRepository(_repoPath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {_repoPath} is not a Git repository");
            Environment.Exit(1);
        }

        // Проверка доступности Gemini CLI
        await AnsiConsole.Status()
            .StartAsync("Checking Gemini CLI availability...", async ctx =>
            {
                var isAvailable = await _reviewProvider.IsAvailableAsync();
                if (!isAvailable)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Gemini CLI is not available. Install it first.");
                    Environment.Exit(1);
                }
                AnsiConsole.MarkupLine("[green]✓[/] Gemini CLI is available");
            });
    }

    private static async Task RunInteractiveMode()
    {
        AnsiConsole.Write(new Rule("[cyan]🔍 Интерактивный режим[/]").LeftJustified());
        AnsiConsole.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<ReviewMode>()
                .Title("[bold cyan]Выберите режим проверки:[/]")
                .PageSize(5)
                .MoreChoicesText("[grey](Используйте ↑↓ для навигации)[/]")
                .AddChoices(
                    ReviewMode.PreCommit,
                    ReviewMode.Branch,
                    ReviewMode.Remote,
                    ReviewMode.Custom
                )
                .UseConverter(mode => mode switch
                {
                    ReviewMode.PreCommit => "📝 Pre-Commit Review (staged files)",
                    ReviewMode.Branch => "🌿 Branch Review (current vs main)",
                    ReviewMode.Remote => "🌐 Remote Review (colleague's branch)",
                    ReviewMode.Custom => "⚙️  Custom Review (manual refs)",
                    _ => throw new ArgumentOutOfRangeException()
                }));

        AnsiConsole.WriteLine();

        switch (choice)
        {
            case ReviewMode.PreCommit:
                await RunPreCommitReview();
                break;

            case ReviewMode.Branch:
                var baseBranch = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan]Base branch:[/]")
                        .DefaultValue("main")
                        .ShowDefaultValue());
                await RunBranchReview(baseBranch);
                break;

            case ReviewMode.Remote:
                var remoteBranch = AnsiConsole.Ask<string>("[cyan]Enter remote branch name:[/]");
                await RunRemoteReview(remoteBranch);
                break;

            case ReviewMode.Custom:
                var baseRef = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan]Base ref:[/]")
                        .DefaultValue("main")
                        .ShowDefaultValue());
                var headRef = AnsiConsole.Prompt(
                    new TextPrompt<string>("[cyan]Head ref:[/]")
                        .DefaultValue("HEAD")
                        .ShowDefaultValue());
                await RunCustomReview(baseRef, headRef);
                break;
        }
    }

    private static Task RunPreCommitReview()
    {
        AnsiConsole.MarkupLine("[cyan]📝 Pre-Commit Review Mode[/]");
        AnsiConsole.MarkupLine("[dim]Checking staged files...[/]\n");

        // TODO: Реализовать проверку staged файлов
        AnsiConsole.MarkupLine("[yellow]⚠️  Pre-commit режим пока не реализован[/]");
        AnsiConsole.MarkupLine("[dim]Будет добавлено в Phase 2[/]");
        return Task.CompletedTask;
    }

    private static async Task RunBranchReview(string baseBranch)
    {
        await RunCustomReview(baseBranch, "HEAD");
    }

    private static Task RunRemoteReview(string remoteBranch)
    {
        AnsiConsole.MarkupLine($"[cyan]🌐 Remote Review Mode[/]");
        AnsiConsole.MarkupLine($"[dim]Checking remote branch: {remoteBranch}[/]\n");

        // TODO: Реализовать fetch удалённой ветки
        AnsiConsole.MarkupLine("[yellow]⚠️  Remote режим пока не реализован[/]");
        AnsiConsole.MarkupLine("[dim]Будет добавлено в Phase 2[/]");
        return Task.CompletedTask;
    }

    private static async Task RunCustomReview(string baseBranch, string headBranch)
    {
        if (_gitService == null || _reviewProvider == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Services not initialized");
            return;
        }

        // Получение текущей ветки
        var currentBranch = await _gitService.GetCurrentBranchAsync(_repoPath);
        AnsiConsole.MarkupLine($"[blue]Current branch:[/] {currentBranch}");
        AnsiConsole.MarkupLine($"[blue]Comparing:[/] {baseBranch}...{headBranch}\n");

        // Получение diff
        DiffResult? diffResult = null;
        try
        {
            await AnsiConsole.Status()
                .StartAsync("Extracting git diff...", async ctx =>
                {
                    diffResult = await _gitService.GetDiffAsync(_repoPath, baseBranch, headBranch);
                    AnsiConsole.MarkupLine($"[green]✓[/] Found {diffResult.Files.Count} changed files");
                });
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return;
        }

        if (diffResult == null || string.IsNullOrWhiteSpace(diffResult.FullDiff))
        {
            AnsiConsole.MarkupLine($"[yellow]No changes found between {baseBranch} and {headBranch}[/]");
            return;
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
        AnsiConsole.WriteLine();

        // Выполнить ревью
        ReviewResult? reviewResult = null;
        await AnsiConsole.Status()
            .StartAsync("Running AI code review...", async ctx =>
            {
                reviewResult = await _reviewProvider.ReviewAsync(diffResult.FullDiff);
            });

        if (reviewResult == null)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Review failed");
            return;
        }

        if (!string.IsNullOrWhiteSpace(reviewResult.Error))
        {
            var escapedError = Markup.Escape(reviewResult.Error);
            AnsiConsole.MarkupLine($"[red]Error:[/] {escapedError}");
            return;
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
    }
}

internal class CommandLineOptions
{
    public string RepoPath { get; set; } = string.Empty;
    public ReviewMode Mode { get; set; } = ReviewMode.Interactive;
    public string? BaseBranch { get; set; }
    public string? HeadBranch { get; set; }
    public string? RemoteBranch { get; set; }
    public string? OutputFile { get; set; }
    public bool ShowHelp { get; set; }
}
