// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Lycia.Messaging;

namespace Lycia.Abstractions;

public interface ISagaStepFluent
{
    Task ThenMarkAsComplete();
    Task ThenMarkAsFailed(FailResponse fail, CancellationToken cancellationToken = default);
    Task ThenMarkAsCompensated(CancellationToken cancellationToken = default);
    Task ThenMarkAsCompensationFailed();
}