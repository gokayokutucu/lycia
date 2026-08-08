// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0

namespace Lycia.Saga;

/// <summary>
/// Resolves the effective CancellationToken for a deferred tracked saga-step operation
/// (<see cref="ReactiveSagaStepFluent{TInitialMessage}"/>, <see cref="CoordinatedSagaStepFluent{TInitialMessage,TSagaData}"/>).
/// The terminal token (passed to a Then... method) is authoritative; the token captured at
/// SendWithTracking/PublishWithTracking/RespondWithTracking/ScheduleWithTracking call time is only used as a
/// fallback when the terminal token is left at its default, preserving source compatibility with the older
/// WithTracking(message, cancellationToken).Then...() call shape.
/// </summary>
internal static class SagaStepFluentToken
{
    public static CancellationToken Resolve(CancellationToken terminal, CancellationToken captured) =>
        terminal != default ? terminal : captured;
}
