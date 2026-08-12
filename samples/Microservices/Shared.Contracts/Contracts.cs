using Lycia.Saga.Abstractions.Messaging;
using Lycia.Saga.Messaging;

namespace Lycia.Samples.Microservices.Contracts;

public interface ICheckoutServiceCommand : ICommand, ICommandEndpoint;
public interface IOrderServiceCommand : ICommand, ICommandEndpoint;
public interface IInventoryServiceCommand : ICommand, ICommandEndpoint;
public interface IPaymentServiceCommand : ICommand, ICommandEndpoint;
public interface IShippingServiceCommand : ICommand, ICommandEndpoint;

public sealed class StartCheckoutCommand : CommandBase, ICheckoutServiceCommand
{
    public Guid OrderId { get; set; }
    public string? FailAt { get; set; }
}
public sealed class CreateOrderCommand : CommandBase, IOrderServiceCommand { public Guid OrderId { get; set; } }
public sealed class ReserveInventoryCommand : CommandBase, IInventoryServiceCommand
{
    public Guid OrderId { get; set; }
    public bool InjectFailure { get; set; }
}
public sealed class ProcessPaymentCommand : CommandBase, IPaymentServiceCommand
{
    public Guid OrderId { get; set; }
    public bool InjectFailure { get; set; }
}
public sealed class ShipOrderCommand : CommandBase, IShippingServiceCommand { public Guid OrderId { get; set; } }

public sealed class OrderCreatedResponse : ResponseBase<CreateOrderCommand> { public Guid OrderId { get; set; } }
public sealed class InventoryReservedResponse : ResponseBase<ReserveInventoryCommand> { public Guid OrderId { get; set; } }
public sealed class PaymentSucceededResponse : ResponseBase<ProcessPaymentCommand> { public Guid OrderId { get; set; } }
public sealed class OrderShippedResponse : ResponseBase<ShipOrderCommand> { public Guid OrderId { get; set; } }

public sealed class CheckoutCompletedEvent : EventBase { public Guid OrderId { get; set; } }

public sealed class CheckoutSagaData : SagaData
{
    public Guid OrderId { get; set; }
    public string Status { get; set; } = "Starting";
    public string? FailAt { get; set; }
}

public sealed class ServiceSagaData : SagaData { public Guid OrderId { get; set; } }
