using Lycia.Saga.Handlers;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Commands;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Events;
using Sample_Net90.Choreography.Domain.Sagas.Payment.ProcessPayment.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process;

public sealed class ProcessPaymentSagaHandler(ILogger<ProcessPaymentSagaHandler> logger, IMapper mapper, IPaymentRepository paymentRepository)
    : StartReactiveSagaHandler<ProcessPaymentSagaCommand>
{
    public override async Task HandleStartAsync(ProcessPaymentSagaCommand message)
    {
        try
        {
            logger.LogInformation("ProcessPaymentSagaHandler => HandleStartAsync => Start processing ProcessPaymentSagaCommand.");

            var Payment = mapper.Map<Domain.Entities.Payment>(message);
            var id = await paymentRepository.ProcessAsync(Payment);
            logger.LogInformation("ProcessPaymentSagaHandler => HandleStartAsync => Payment Processed with ID: {PaymentId}", id);

            Payment.Id = id;
            var PaymentProcessdEvent = mapper.Map<PaymentProcessedSagaEvent>(Payment);
            await Context.PublishWithTracking(PaymentProcessdEvent).ThenMarkAsComplete();
            logger.LogInformation("ProcessPaymentSagaHandler => HandleStartAsync => PaymentProcessedSagaEvent published successfully and ProcessPaymentSagaCommand marked as complete for PaymentId: {PaymentId}", id);
        }
        catch (Exception ex)
        {
            await Context.MarkAsFailed<ProcessPaymentSagaCommand>();
            logger.LogError(ex, "ProcessPaymentSagaHandler => HandleStartAsync => Error processing ProcessPaymentSagaCommand.");

            throw new Exception($"ProcessPaymentSagaHandler => HandleStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    public override async Task CompensateStartAsync(ProcessPaymentSagaCommand message)
    {
        try
        {
            logger.LogInformation("ProcessPaymentSagaHandler => CompensateStartAsync => Start compensating ProcessPaymentSagaCommand.");

            var Payment = mapper.Map<Domain.Entities.Payment>(message);
            await paymentRepository.RefundAsync(Payment);
            logger.LogInformation("ProcessPaymentSagaHandler => CompensateStartAsync => Payment with ID: {PaymentId} deleted successfully.", Payment.Id);

            await Context.MarkAsCompensated<ProcessPaymentSagaCommand>();
            logger.LogInformation("ProcessPaymentSagaHandler => CompensateStartAsync => Compensation completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ProcessPaymentSagaHandler => CompensateStartAsync => Error during compensation of ProcessPaymentSagaCommand.");

            await Context.MarkAsCompensationFailed<ProcessPaymentSagaCommand>();
            logger.LogError("ProcessPaymentSagaHandler => CompensateStartAsync => Error processing ProcessPaymentSagaCommand compensation.");

            throw new Exception($"ProcessPaymentSagaHandler => CompensateStartAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }
