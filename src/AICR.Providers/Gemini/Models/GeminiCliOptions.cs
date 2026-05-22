using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AICR.Providers.Gemini.Models;

public class GeminiCliOptions
{
    public string ExecutablePath { get; set; } = "gemini";
    public string? Model { get; set; }
    public string OutputFormat { get; set; } = "text";
    public int TimeoutSeconds { get; set; } = 120;
}
