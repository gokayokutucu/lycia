// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
namespace Lycia.Tests.Messages;

/// <summary>
/// xUnit collection for every test class that asserts on the shared static
/// <c>GrandparentCompensationHandler.Invocations</c> / <c>ParentCompensationHandler.Invocations</c> /
/// <c>ChildCompensationHandler.Invocations</c> lists in this file. xUnit runs different test classes in
/// parallel by default; sharing this collection forces those classes to run sequentially relative to each
/// other, so one test's <c>Invocations.Clear()</c> and assertions can never race another's. Add
/// <c>[Collection(CompensationHandlerFixtureCollection.Name)]</c> to any test class that reads or clears
/// these static lists.
/// </summary>
[CollectionDefinition(Name)]
public class CompensationHandlerFixtureCollection
{
    public const string Name = "SharedCompensationHandlerFixtures";
}
