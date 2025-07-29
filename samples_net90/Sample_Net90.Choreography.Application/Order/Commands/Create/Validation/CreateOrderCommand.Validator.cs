using FluentValidation;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        //RuleFor(x => x.CustomerId)
        //    .NotEmpty()
        //    .WithMessage("Customer ID is required.");
        //RuleFor(x => x.OrderItems)
        //    .NotEmpty()
        //    .WithMessage("Order items cannot be empty.")
        //    .Must(items => items.All(item => item.Quantity > 0))
        //    .WithMessage("All order items must have a quantity greater than zero.");
    }
}
