// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Reflection;

namespace Lycia.Extensions;

/// <summary>
/// Fluent saga-discovery DSL reached via <see cref="LyciaBuilder.AddSagas()"/>. Delegates to the existing
/// <see cref="LyciaBuilder"/> discovery methods; it does not implement a second discovery path.
/// </summary>
public sealed class LyciaSagaBuilder
{
    private readonly LyciaBuilder _builder;

    internal LyciaSagaBuilder(LyciaBuilder builder) => _builder = builder;

    /// <summary>Discovers and registers saga handlers from the assembly that calls this method.</summary>
    public LyciaBuilder FromCurrentAssembly()
    {
        // Captured here (not inside LyciaBuilder) so the immediate caller is the consumer's assembly.
        var calling = Assembly.GetCallingAssembly();
        return _builder.AddSagasFromAssemblies(calling);
    }

    /// <summary>Discovers and registers saga handlers from the assemblies containing the given marker types.</summary>
    public LyciaBuilder FromAssembliesOf(params Type[] markerTypes) => _builder.AddSagasFromAssembliesOf(markerTypes);

    /// <summary>Discovers and registers saga handlers from the given assemblies.</summary>
    public LyciaBuilder FromAssemblies(params Assembly[] assemblies) => _builder.AddSagasFromAssemblies(assemblies);
}
