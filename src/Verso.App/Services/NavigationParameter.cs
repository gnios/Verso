using System;
using Verso.Core.Services;

namespace Verso.App.Services;

public sealed record NavigationParameter(
    LibraryStatusFilter? StatusFilter = null,
    int? TagId = null,
    int? FolderId = null,
    Guid? TranscriptionId = null,
    bool UnassignedOnly = false);
