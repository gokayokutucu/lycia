using Lycia.Extensions;
using Lycia.Extensions.RabbitMq;
using Lycia.Persistence.PostgreSql;
using Lycia.Persistence.Redis;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Reconciliation;
using Lycia.Samples.Microservices.Contracts;
using Npgsql;

namespace Lycia.Samples.Microservices;

internal static class ServiceBootstrap
{
    public static async Task RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var applicationId = Environment.GetEnvironmentVariable("APPLICATION_ID")
            ?? throw new InvalidOperationException("APPLICATION_ID is required.");
        var postgres = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
            ?? throw new InvalidOperationException("POSTGRES_CONNECTION is required.");
        var redis = Environment.GetEnvironmentVariable("REDIS_CONNECTION")
            ?? throw new InvalidOperationException("REDIS_CONNECTION is required.");
        var rabbit = Environment.GetEnvironmentVariable("RABBITMQ_CONNECTION")
            ?? "amqp://guest:guest@rabbitmq:5672";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["ApplicationId"] = applicationId });

        builder.Services.AddLycia(builder.Configuration, lycia =>
        {
            lycia.AddSagas().FromCurrentAssembly();
            lycia.UseTransport().RabbitMq(o => { o.ApplicationId=applicationId; o.ConnectionString=rabbit; });
            lycia.UsePersistence()
                .WithPostgreSqlCanonicalSagaStore(o => o.ConnectionString=postgres)
                .WithPostgreSqlInbox(o => o.ConnectionString=postgres)
                .WithPostgreSqlOutbox(o => o.ConnectionString=postgres)
                .WithRedisOperationalSagaStore(o => { o.ApplicationId=applicationId; o.ConnectionString=redis; })
                .RequireAtomicBoundary()
                .UseSplitStore();
        });

        var app=builder.Build();
        app.MapGet("/health",(IPersistenceTopology topology)=>Results.Ok(new { status="healthy",applicationId,
            persistenceMode=topology.Current.Mode.ToString(),canonicalStore=topology.Current.CanonicalStore,
            operationalStore=topology.Current.OperationalStore,reconciliation=topology.Current.ReconciliationEnabled }));
        app.MapGet("/state/{orderId:guid}",async (Guid orderId,CancellationToken token)=>
        {
            await using var connection=new NpgsqlConnection(postgres); await connection.OpenAsync(token);
            await using var command=connection.CreateCommand();
            command.CommandText="SELECT saga_id,version,data_json::text FROM lycia_saga_data WHERE data_json->>'OrderId'=@orderId ORDER BY version DESC LIMIT 1";
            command.Parameters.AddWithValue("orderId",orderId.ToString());
            await using var reader=await command.ExecuteReaderAsync(token);
            return await reader.ReadAsync(token)?Results.Ok(new { sagaId=reader.GetGuid(0),sagaVersion=reader.GetInt64(1),canonicalState=reader.GetString(2) }):Results.NotFound();
        });
        app.MapPost("/debug/projections/{sagaId:guid}/restore",async(Guid sagaId,ISagaProjectionReconciler reconciler,CancellationToken token)=>
            await reconciler.RestoreLatestAsync(sagaId,token)?Results.Accepted():Results.NotFound());
        app.MapDelete("/debug/projections/{sagaId:guid}",async(Guid sagaId,IOperationalSagaProjectionStore store,CancellationToken token)=>
        { await store.DeleteAsync(sagaId,token); return Results.NoContent(); });
        if (applicationId == "CheckoutService")
        {
            app.MapPost("/checkout",async (CheckoutRequest request,IEventBus bus,CancellationToken token)=>
            {
                var orderId=request.OrderId==Guid.Empty?Guid.NewGuid():request.OrderId;
                var command=new StartCheckoutCommand{OrderId=orderId,FailAt=request.FailAt};
                if (request.MessageId is { } messageId)
                    command.MessageId=messageId;
                await bus.Send(command,cancellationToken:token);
                return Results.Accepted($"/checkouts/{orderId}",new {orderId,messageId=command.MessageId});
            });
            app.MapGet("/checkouts/{orderId:guid}",async(Guid orderId,CancellationToken token)=>
            {
                await using var connection=new NpgsqlConnection(postgres); await connection.OpenAsync(token);
                await using var command=connection.CreateCommand(); command.CommandText="SELECT saga_id,version,data_json::text FROM lycia_saga_data WHERE data_json->>'OrderId'=@orderId ORDER BY version DESC LIMIT 1"; command.Parameters.AddWithValue("orderId",orderId.ToString());
                await using var reader=await command.ExecuteReaderAsync(token); if(!await reader.ReadAsync(token)) return Results.NotFound();
                var sagaId=reader.GetGuid(0); var version=reader.GetInt64(1); using var scope=app.Services.CreateScope(); var projection=scope.ServiceProvider.GetRequiredService<IOperationalSagaProjectionStore>();
                return Results.Ok(new {orderId,sagaId,sagaVersion=version,canonicalState=reader.GetString(2),operationalProjectionVersion=await projection.GetVersionAsync(sagaId,token)});
            });
        }
        await app.RunAsync();
    }

}

internal sealed record CheckoutRequest(Guid OrderId, Guid? MessageId, string? FailAt);
