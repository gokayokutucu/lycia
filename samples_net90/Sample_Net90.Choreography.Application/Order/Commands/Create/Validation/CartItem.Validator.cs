using FluentValidation;
using Sample_Net90.Choreography.Domain.Constants;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class CartItemValidator : AbstractValidator<CartItem>
{
    public CartItemValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage(ValidationErrors.IsRequired)
            .NotEqual(Guid.Empty).WithMessage(ValidationErrors.MustBeCorrectFormat);

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage(ValidationErrors.MustBeGreaterThan);
    }
}
