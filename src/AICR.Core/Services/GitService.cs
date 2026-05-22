using AICR.Core.Interfaces;
using AICR.Core.Models;
using LibGit2Sharp;

namespace AICR.Core.Services;

public class GitService : IGitService
{
    public bool IsGitRepository(string path)
    {
        try
        {
            return Repository.IsValid(path);
        }
        catch
        {
            return false;
        }
    }

    public Task<string> GetCurrentBranchAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            using var repo = new Repository(repositoryPath);
            return repo.Head.FriendlyName;
        }, cancellationToken);
    }

    public Task<DiffResult> GetDiffAsync(
      string repositoryPath,
      string baseBranch = "main",
      string headBranch = "HEAD",
      CancellationToken cancellationToken = default)
  {
      return Task.Run(() =>
      {
          using var repo = new Repository(repositoryPath);

          // Резолв base commit
          Commit? baseCommit = ResolveCommit(repo, baseBranch);
          if (baseCommit == null)
          {
              throw new InvalidOperationException(
                  $"Base ref '{baseBranch}' not found. Available branches: {string.Join(", ",
  repo.Branches.Select(b => b.FriendlyName))}");
          }

          // Резолв head commit
          Commit? headCommit = ResolveCommit(repo, headBranch);
          if (headCommit == null)
          {
              throw new InvalidOperationException($"Head ref '{headBranch}' not found");
          }

          // Получить diff
          var compareOptions = new CompareOptions
          {
              Similarity = SimilarityOptions.Renames
          };

          var diff = repo.Diff.Compare<TreeChanges>(
              baseCommit.Tree,
              headCommit.Tree,
              compareOptions);

          // Получить патчи
          var patch = repo.Diff.Compare<Patch>(
              baseCommit.Tree,
              headCommit.Tree,
              compareOptions);

          var files = new List<FileDiff>();

          foreach (var change in diff)
          {
              var filePatch = patch[change.Path];

              files.Add(new FileDiff(
                  FilePath: change.Path,
                  Status: change.Status.ToString(),
                  LinesAdded: filePatch?.LinesAdded ?? 0,
                  LinesRemoved: filePatch?.LinesDeleted ?? 0,
                  Patch: filePatch?.Patch ?? string.Empty
              ));
          }

          var headBranchName = headBranch == "HEAD" ? repo.Head.FriendlyName : headBranch;

          return new DiffResult(
              BaseBranch: baseBranch,
              HeadBranch: headBranchName,
              FullDiff: patch.Content,
              Files: files
          );

      }, cancellationToken);
  }

  private static Commit? ResolveCommit(Repository repo, string reference)
  {
      // Попробовать как прямой reference (HEAD, HEAD~1, SHA, etc)
      try
      {
          var gitObject = repo.Lookup(reference);

          if (gitObject is Commit commit)
          {
              return commit;
          }

          if (gitObject is TagAnnotation tag)
          {
              return tag.Target as Commit;
          }
      }
      catch
      {
          // Игнорируем, попробуем другие варианты
      }

      // Попробовать как ветку
      var branch = repo.Branches[reference];
      if (branch != null)
      {
          return branch.Tip;
      }

      // Fallback для main -> master
      if (reference == "main")
      {
          branch = repo.Branches["master"];
          if (branch != null)
          {
              return branch.Tip;
          }
      }

      return null;
  }
}
