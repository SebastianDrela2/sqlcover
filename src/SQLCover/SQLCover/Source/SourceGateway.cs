﻿using System.Collections.Generic;
using SQLCover.Objects;

namespace SQLCover.Source
{
    public interface SourceGateway
    {
        SqlServerVersion GetVersion();
        IEnumerable<Batch> GetBatches(List<string> objectFilter,List<string> filteredObjects = null);
        string GetWarnings();
    }
}