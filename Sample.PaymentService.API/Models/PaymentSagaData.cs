using Lycia.Saga; // For SagaData

namespace Sample.PaymentService.API.Models
{
    /// <summary>
    /// Concrete implementation of SagaData for payment service related sagas.
    /// </summary>
    public class PaymentSagaData : SagaData
    {
        // You can add specific strongly-typed properties here if desired,
        // or continue to use the Extras dictionary from the base class.
        // For this task, we'll primarily use Extras as specified.

        public PaymentSagaData() : base()
        {
            // Initialize any default values if necessary
        }
    }
}
