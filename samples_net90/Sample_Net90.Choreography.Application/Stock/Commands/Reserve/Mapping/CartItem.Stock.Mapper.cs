using Mapster;
using Sample_Net90.Choreography.Domain.Enums;

namespace Sample_Net90.Choreography.Application.Stock.Commands.Reserve;

public sealed class CartItemToStockMapper : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Domain.Entities.CartItem, Domain.Entities.Stock>()
            .Map(to => to.StockId, from => Guid.CreateVersion7())
            ;
    }
}
