using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AICR.Core.Models;

public record FileDiff(
    string FilePath,
    string Status,      // "Added", "Modified", "Deleted"
    int LinesAdded,
    int LinesRemoved,
    string Patch
);
