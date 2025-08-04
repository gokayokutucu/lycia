using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Sample_Net90.Choreography.Application.Interfaces.Repositories;
using Sample_Net90.Choreography.Domain.Entities;

namespace Sample_Net90.Choreography.Infrastructure.Repositories;

public sealed class OrderRepository (ILogger<OrderRepository> logger, SqlConnection connection)
    : IOrderRepository
{
    public async Task<Guid> CreateAsync(Order order)
    {
        try
        {
            logger.LogInformation("OrderRepository => CreateAsync => Start processing CreateAsync for Order ID: {OrderId}", order.Id);
            var sql = "";
            connection.Execute(sql, order);
            logger.LogInformation("OrderRepository => CreateAsync => Order created with ID: {OrderId}", order.Id);

            return order.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OrderRepository => CreateAsync => Error processing CreateAsync for Order ID: {OrderId}", order.Id);
            throw new Exception($"OrderRepository => CreateAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
        finally
        {
            logger.LogInformation("OrderRepository => CreateAsync => End processing CreateAsync.");
        }
    }

    public async Task DeleteAsync(Order order)
    {
        try
        {
            logger.LogInformation("OrderRepository => DeleteAsync => Start processing DeleteAsync for Order ID: {OrderId}", order.Id);
            var sql = "";
            connection.Execute(sql, new { order.Id });
            logger.LogInformation("OrderRepository => DeleteAsync => Order with ID: {OrderId} deleted successfully.", order.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OrderRepository => DeleteAsync => Error processing DeleteAsync for Order ID: {OrderId}", order.Id);
            throw new Exception($"OrderRepository => DeleteAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
        finally
        {
            logger.LogInformation("OrderRepository => DeleteAsync => End processing DeleteAsync.");
        }
    }

    public async Task<bool> OrderExistsAsync(Guid orderId)
    {
        try
        {
            logger.LogInformation("OrderRepository => OrderExistsAsync => Start processing OrderExistsAsync for Order ID: {OrderId}", orderId);
            var sql = "SELECT COUNT(1) FROM Orders WHERE Id = @OrderId";
            var exists = connection.ExecuteScalar<bool>(sql, new { OrderId = orderId });
            logger.LogInformation("OrderRepository => OrderExistsAsync => Order with ID: {OrderId} exists: {Exists}", orderId, exists);
            return exists;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OrderRepository => OrderExistsAsync => Error processing OrderExistsAsync for Order ID: {OrderId}", orderId);
            throw new Exception($"OrderRepository => OrderExistsAsync => Error : {ex.InnerException?.Message ?? ex.Message}", ex);
        }
        finally
        {
            logger.LogInformation("OrderRepository => OrderExistsAsync => End processing OrderExistsAsync.");
        }
    }
}
