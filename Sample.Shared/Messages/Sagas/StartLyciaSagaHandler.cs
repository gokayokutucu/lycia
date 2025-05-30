using Lycia.Saga.Handlers;
using Sample.Shared.Messages.Commands;
using Sample.Shared.Messages.Events;
using System.Threading.Tasks;
using Lycia.Saga.Abstractions; // For ISagaContext extensions like PublishWithTracking & ThenMarkAsComplete

namespace Sample.Shared.Messages.Sagas
{
    public class StartLyciaSagaHandler : StartReactiveSagaHandler<StartLyciaSagaCommand, LyciaSagaData>
    {
        public override async Task HandleStartAsync(StartLyciaSagaCommand command)
        {
            SagaData.OrderId = command.OrderId;
            SagaData.UserId = command.UserId;
            SagaData.TotalPrice = command.TotalPrice;
            SagaData.Items = command.Items;
            SagaData.CardDetails = command.CardDetails;
            SagaData.ShippingAddress = command.ShippingAddress;
            SagaData.UserEmail = command.UserEmail;
            SagaData.OrderStatus = "SagaStarted";

            var sagaStartedEvent = new LyciaSagaStartedEvent
            {
                OrderId = SagaData.OrderId,
                UserId = SagaData.UserId,
                TotalPrice = SagaData.TotalPrice
            };

            await Context.PublishWithTracking(sagaStartedEvent)
                         .ThenMarkAsComplete();
        }
    }
}
