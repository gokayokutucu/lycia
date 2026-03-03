using Mapster;
using Shared.Contracts.Commands;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Commands.Create;

public sealed class CreateOrderCommandMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<CreateOrderCommand, CreateOrderSagaCommand>()
            .Map(dest => dest.CustomerId, src => src.CustomerId)
            .Map(dest => dest.Items, src => src.Items)
            .Map(dest => dest.ShippingAddressId, src => src.AddressId)
            .Map(dest => dest.SavedCardId, src => src.CardId);
    }
}
