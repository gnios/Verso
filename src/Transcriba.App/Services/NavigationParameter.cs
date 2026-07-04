using System;
using Transcriba.Core.Services;

namespace Transcriba.App.Services;

public sealed record NavigationParameter(
    LibraryStatusFilter? StatusFilter = null,
    int? TagId = null,
    int? ResearchId = null,
    Guid? TranscriptionId = null);
