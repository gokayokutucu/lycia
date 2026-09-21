using Lycia.Saga.Abstractions.Persistence.Reconciliation;

namespace Lycia.Persistence.Redis.Tests;

[Collection(RedisSagaStoreCollection.Name)]
public sealed class RedisOperationalSagaProjectionStoreTests(RedisSagaStoreFixture fixture)
{
    [Fact]
    public async Task Apply_is_idempotent_and_stale_state_cannot_overwrite_newer_projection()
    {
        var store=new RedisOperationalSagaProjectionStore(fixture.Database); var sagaId=Guid.NewGuid();
        var v2=Intent(sagaId,2,"two");
        Assert.Equal(ProjectionApplyOutcome.Applied,await store.ApplyAsync(v2));
        Assert.Equal(ProjectionApplyOutcome.AlreadyApplied,await store.ApplyAsync(v2));
        Assert.Equal(ProjectionApplyOutcome.Superseded,await store.ApplyAsync(Intent(sagaId,1,"one")));
        Assert.Equal(2,await store.GetVersionAsync(sagaId));
        await store.DeleteAsync(sagaId);
        Assert.Equal(0,await store.GetVersionAsync(sagaId));
    }

    [Fact]
    public async Task Newer_complete_projection_can_arrive_before_its_predecessor()
    {
        var store=new RedisOperationalSagaProjectionStore(fixture.Database); var sagaId=Guid.NewGuid();
        Assert.Equal(ProjectionApplyOutcome.Applied,await store.ApplyAsync(Intent(sagaId,11,"latest")));
        Assert.Equal(ProjectionApplyOutcome.Superseded,await store.ApplyAsync(Intent(sagaId,10,"older")));
        Assert.Equal(11,await store.GetVersionAsync(sagaId));
        await store.DeleteAsync(sagaId);
    }

    private static SagaProjectionIntent Intent(Guid sagaId,long version,string status)=>new()
    { TransitionId=Guid.NewGuid(),SagaId=sagaId,ExpectedVersion=Math.Max(0,version-1),TargetVersion=version,
      SagaDataType="Test",Payload=$"{{\"SagaId\":\"{sagaId}\",\"Version\":{version},\"Status\":\"{status}\"}}" };
}
