namespace OrderService.Application.Features.Orders.Commands;

public sealed record DeleteOrderCommandResult
{
    public bool IsSuccess { get; init; }
    public static DeleteOrderCommandResult Create(bool isSuccess) 
        => new DeleteOrderCommandResult
        {
            IsSuccess = isSuccess
        };
}
