using FluentValidation;

namespace Sample_Net90.Choreography.Application.Payment.Commands.Process;

public sealed class ProcessPaymentCommandValidator : AbstractValidator<ProcessPaymentCommand>
{
    public ProcessPaymentCommandValidator()
    {
        // Add validation rules here
        // For example:
        // RuleFor(command => command.OrderId).NotEmpty().WithMessage("OrderId is required.");
        // RuleFor(command => command.Amount).GreaterThan(0).WithMessage("Amount must be greater than zero.");
    }
}
