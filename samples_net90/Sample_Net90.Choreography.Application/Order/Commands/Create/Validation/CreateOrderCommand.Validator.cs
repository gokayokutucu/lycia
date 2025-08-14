using FluentValidation;
using Sample_Net90.Choreography.Domain.Constants;

namespace Sample_Net90.Choreography.Application.Order.Commands.Create;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage(ValidationErrors.IsRequired)
            .NotEqual(Guid.Empty).WithMessage(ValidationErrors.MustBeCorrectFormat);

        RuleFor(x => x.DeliveryAddress)
            .NotEmpty().WithMessage(ValidationErrors.IsRequired)
            .NotEqual(Guid.Empty).WithMessage(ValidationErrors.MustBeCorrectFormat);

        RuleFor(x => x.BillingAddress)
            .NotEmpty().WithMessage(ValidationErrors.IsRequired)
            .NotEqual(Guid.Empty).WithMessage(ValidationErrors.MustBeCorrectFormat);

        RuleFor(x => x.Cart)
            .NotEmpty().WithMessage(ValidationErrors.MustNotBeEmpty);

        RuleForEach(x => x.Cart).SetValidator(new CartItemValidator());
    }
}
