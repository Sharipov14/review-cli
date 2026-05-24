using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AICR.Providers.Abstractions;
using AICR.Providers.Gemini.Models;

namespace AICR.Providers.Gemini;

public class GeminiCliProvider : IReviewProvider
{
    private readonly GeminiCliOptions _options;

    public string Name => "Gemini CLI Provider";

    public GeminiCliProvider(GeminiCliOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this._options = options;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = this._options.ExecutablePath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(ct);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="diff"></param>
    /// <param name="customRules"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<ReviewResult> ReviewAsync(string diff, string? customRules = null, CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            var prompt = BuildPrompt(diff, customRules);
            var response = await ExecuteGeminiAsync(prompt, ct);
            var issues = ParseResponse(response);

            return new ReviewResult(
                ProviderName: Name,
                ModelName: _options.Model ?? "gemini-2.0-flash-exp",
                Duration: DateTime.UtcNow - startTime,
                Issues: issues,
                RawResponse: response
            );
        }
        catch (Exception ex)
        {
            return new ReviewResult(
                ProviderName: Name,
                ModelName: _options.Model ?? "unknown",
                Duration: DateTime.UtcNow - startTime,
                Issues: new List<ReviewIssue>(),
                RawResponse: string.Empty,
                Error: ex.Message
            );
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="diff"></param>
    /// <param name="customRules"></param>
    /// <returns></returns>
    private string BuildPrompt(string diff, string? customRules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a senior code reviewer. Analyze the following git diff and provide feedback.");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(customRules))
        {
            sb.AppendLine("## Project-specific rules:");
            sb.AppendLine(customRules);
            sb.AppendLine();
        }

        sb.AppendLine("## Instructions:");
        sb.AppendLine("- Focus on logic errors, security issues, performance problems, and code style");
        sb.AppendLine("- Provide specific line numbers when possible");
        sb.AppendLine("- Suggest concrete improvements");
        sb.AppendLine("- Format your response as JSON with this structure:");
        sb.AppendLine(@"{
            ""issues"": [
                {
                    ""severity"": ""error|warning|info"",
                    ""category"": ""security|performance|style|logic"",
                    ""message"": ""Description of the issue"",
                    ""filePath"": ""path/to/file.cs"",
                    ""lineNumber"": 42,
                    ""suggestion"": ""How to fix it""
                }
            ]
        }");

        sb.AppendLine();
        sb.AppendLine("## Git Diff:");
        sb.AppendLine("```diff");
        sb.AppendLine(diff);
        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="prompt"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<string> ExecuteGeminiAsync(string prompt, CancellationToken ct)
    {
        var process = new Process
        {
            StartInfo = GetProcessStartInfo(prompt)
        };

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            throw new Exception($"Gemini CLI failed: {error}");
        }

        return output.ToString();
    }

    private ProcessStartInfo GetProcessStartInfo(string prompt)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = this._options.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var args = BuildArguments(prompt);

        processStartInfo.ArgumentList.Add("-p");
        processStartInfo.ArgumentList.Add($"\"{prompt.Replace("\"", "\\\"")}\"");
        processStartInfo.ArgumentList.Add("-y"); // YOLO mode - auto-approve

        if (!string.IsNullOrWhiteSpace(_options.Model))
        {
            processStartInfo.ArgumentList.Add("-m");
            processStartInfo.ArgumentList.Add(_options.Model);
        }

        if (_options.OutputFormat == "json")
        {
            processStartInfo.ArgumentList.Add("--output-format");
            processStartInfo.ArgumentList.Add("json");
        }
        return processStartInfo;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="prompt"></param>
    /// <returns></returns>
    private string[] BuildArguments(string prompt)
    {
        var args = new List<string>
        {
            "-p", 
            //"Hello",
            $"\"{prompt.Replace("\"", "\\\"")}\"",
            "-y" // YOLO mode - auto-approve
        };

        // Добавляем модель только если она указана
        if (!string.IsNullOrWhiteSpace(_options.Model))
        {
            args.Add("-m");
            args.Add(_options.Model);
        }

        if (_options.OutputFormat == "json")
        {
            args.Add("--output-format");
            args.Add("json");
        }

        return args.ToArray();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="response"></param>
    /// <returns></returns>
    private List<ReviewIssue> ParseResponse(string response)
    {
        try
        {
            // Попытка распарсить JSON
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var result = JsonSerializer.Deserialize<GeminiResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return result?.Issues ?? new List<ReviewIssue>();
            }

            // Fallback: создать одно issue с полным текстом
            return new List<ReviewIssue>
            {
                new ReviewIssue(
                    Severity: "info",
                    Category: "general",
                    Message: response.Trim()
                )
            };
        }
        catch
        {
            return new List<ReviewIssue>
            {
                new ReviewIssue(
                    Severity: "info",
                    Category: "general",
                    Message: response.Trim()
                )
            };
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private class GeminiResponse
    {
        /// <summary>
        /// 
        /// </summary>
        public List<ReviewIssue>? Issues { get; set; }
    }
}
