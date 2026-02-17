using Mapster;

namespace Sample.Payment.NetFramework481.Application.Payments.UseCases.Commands.Process;

/// <summary>
/// Mapster configuration for ProcessPaymentCommand use case.
/// </summary>
public static class ProcessPaymentCommandMapper
{
    static ProcessPaymentCommandMapper()
    {
        TypeAdapterConfig<Domain.Payments.Payment, ProcessPaymentCommandResult>
            .NewConfig()
            .Map(dest => dest.PaymentId, src => src.Id)
            .Map(dest => dest.OrderId, src => src.OrderId)
            .Map(dest => dest.Status, src => src.Status)
            .Map(dest => dest.TransactionId, src => src.TransactionId)
            .Map(dest => dest.PaidAt, src => src.PaidAt)
            .Map(dest => dest.FailureReason, src => src.FailureReason);
    }
}
