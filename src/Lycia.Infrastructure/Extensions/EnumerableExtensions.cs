namespace Lycia.Infrastructure.Extensions;

using System;
using System.Collections.Generic;

public static class EnumerableExtensions
{
    /// <summary>
    /// Returns distinct elements from a sequence by using a specified key selector.
    /// </summary>
    public static IEnumerable<TSource?> DistinctByKey<TSource, TKey>(
        this IEnumerable<TSource?> source,
        Func<TSource, TKey> keySelector)
    {
        var seenKeys = new HashSet<TKey>();
        foreach (var element in source)
        {
            if (seenKeys.Add(keySelector(element)))
            {
                yield return element;
            }
        }
    }
}