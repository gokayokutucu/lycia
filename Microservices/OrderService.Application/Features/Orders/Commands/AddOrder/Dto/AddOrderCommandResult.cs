namespace OrderService.Application.Features.Orders.Commands;

public sealed record AddOrderCommandResult
{
    public bool IsSuccess { get; init; }
    public static AddOrderCommandResult Create(bool isSuccess) 
        => new AddOrderCommandResult
        {
            IsSuccess = isSuccess
        };
}
