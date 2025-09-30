// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

namespace Lycia.Extensions;

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