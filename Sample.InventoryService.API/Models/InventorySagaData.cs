using Lycia.Saga; // For SagaData

namespace Sample.InventoryService.API.Models
{
    /// <summary>
    /// Concrete implementation of SagaData for inventory service sagas.
    /// </summary>
    public class InventorySagaData : SagaData
    {
        // You can add specific strongly-typed properties here if desired,
        // or continue to use the Extras dictionary from the base class.
        // For this task, we'll primarily use Extras as specified.

        public InventorySagaData() : base()
        {
            // Initialize any default values if necessary
        }
    }
}
