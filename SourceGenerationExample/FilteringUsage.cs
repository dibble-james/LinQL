#pragma warning disable CA1852 // Seal internal types

using System;
using System.Linq;
using System.Linq.Expressions;
using Filtering.Client;

namespace SourceGenerationExample;

/// <summary>
/// Not executed: it exists so the generated filter companion and builders have to compile.
/// </summary>
internal static class FilteringUsage
{
    private static readonly string[] Values = ["widget"];

    internal static Expression<Func<Query, object>> Filtered()
        => q => q
            .FilterProducts(f => f.Name(n => n.Contains("widget")).Price(p => p.Gt(10)).Name(n => n.In(Values)))
            .Select(p => new { p.Id });
}
