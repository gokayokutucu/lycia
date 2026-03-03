using FluentValidation;

namespace Sample.Order.NetFramework481.Application.Orders.UseCases.Queries.Get;

/// <summary>
/// Validator for GetOrderQuery.
/// </summary>
public sealed class GetOrderQueryValidator : AbstractValidator<GetOrderQuery>
{
    public GetOrderQueryValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithMessage("Order ID is required");
    }
}
