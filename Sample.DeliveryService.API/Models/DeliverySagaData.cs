using Lycia.Saga; // For SagaData

namespace Sample.DeliveryService.API.Models
{
    /// <summary>
    /// Concrete implementation of SagaData for delivery service related sagas.
    /// </summary>
    public class DeliverySagaData : SagaData
    {
        // You can add specific strongly-typed properties here if desired,
        // or continue to use the Extras dictionary from the base class.
        // For this task, we'll primarily use Extras as specified.

        public DeliverySagaData() : base()
        {
            // Initialize any default values if necessary
        }
    }
}
