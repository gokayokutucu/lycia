namespace OrderService.Application.Features.Orders.Commands;

public sealed record UpdateOrderCommandResult
{
    public bool IsSuccess { get; init; }
    public static UpdateOrderCommandResult Create(bool isSuccess) 
        => new UpdateOrderCommandResult
        {
            IsSuccess = isSuccess
        };
}