using Lycia.Saga; // For SagaData

namespace Sample.OrderService.API.Models
{
    /// <summary>
    /// Concrete implementation of SagaData for order placement sagas.
    /// </summary>
    public class OrderSagaData : SagaData
    {
        // You can add specific strongly-typed properties here if desired,
        // or continue to use the Extras dictionary from the base class.
        // For this task, we'll primarily use Extras as specified.

        public OrderSagaData() : base()
        {
            // Initialize any default values if necessary
        }
    }
}
