using FluentValidation;

namespace Sample.Payment.NetFramework481.Application.Payments.UseCases.Commands.Process;

/// <summary>
/// Validator for ProcessPaymentCommand.
/// </summary>
public sealed class ProcessPaymentCommandValidator : AbstractValidator<ProcessPaymentCommand>
{
    public ProcessPaymentCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");
    }
}
