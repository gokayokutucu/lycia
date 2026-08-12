using Lycia.Extensions;
using Lycia.Extensions.RabbitMq;
using Lycia.Persistence.PostgreSql;
using Lycia.Persistence.Redis;
using Lycia.Saga.Abstractions;
using Lycia.Saga.Abstractions.Persistence;
using Lycia.Saga.Abstractions.Persistence.Journal;
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
        // Phase 6: canonical journal inspection. Safe metadata only - no payload/SagaData dump by default.
        app.MapGet("/debug/sagas/{sagaId:guid}/journal",async(Guid sagaId,ISagaJournalStore journal,CancellationToken token)=>
        {
            var entries=await journal.ReadAsync(sagaId,afterVersion:0,maxCount:500,token);
            return Results.Ok(entries.Select(e=>new {
                sequence=e.SequenceNumber,previousVersion=e.PreviousVersion,targetVersion=e.TargetVersion,
                transitionType=e.TransitionType.ToString(),messageId=e.MessageId,handlerType=e.HandlerType,
                messageType=e.MessageType,journalSchemaVersion=e.JournalSchemaVersion,createdAtUtc=e.CreatedAtUtc
            }));
        });
        // Phase 6: rebuilds the operational (Redis) projection strictly from canonical journal history via
        // the deterministic reducer - distinct from /debug/projections/{sagaId}/restore, which is the Phase 5
        // reconciliation-based restore from the latest canonical row, not ordered journal replay.
        app.MapPost("/debug/sagas/{sagaId:guid}/rebuild-from-journal",async(Guid sagaId,ISagaRebuildService rebuildService,CancellationToken token)=>
        {
            var outcome=await rebuildService.RebuildSagaAsync(sagaId,token);
            return outcome.Succeeded?Results.Ok(new{sagaId,rebuiltVersion=outcome.RebuiltVersion}):
                Results.UnprocessableEntity(new{sagaId,failureKind=outcome.FailureKind.ToString(),outcome.FailureReason});
        });
        app.MapGet("/debug/sagas/{sagaId:guid}/verify",async(Guid sagaId,ISagaRebuildService rebuildService,CancellationToken token)=>
        {
            var result=await rebuildService.VerifySagaAsync(sagaId,token);
            return Results.Ok(new{sagaId,status=result.Status.ToString(),journalVersion=result.JournalVersion,
                operationalProjectionVersion=result.OperationalProjectionVersion,canonicalVersion=result.CanonicalVersion,result.Detail});
        });
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
