using Lycia.Saga.Extensions;
using Lycia.Saga.Messaging;

namespace Lycia.Tests;

public sealed class MessageIdentityExtensionsTests
{
    [Fact]
    public void Prepare_response_preserves_the_requesting_service_endpoint()
    {
        var request=new TestCommand { ResponseEndpoint="CheckoutService" };
        var response=new TestResponse();
        response.PrepareResponse(request,Guid.NewGuid(),"OrderService");
        Assert.Equal("checkoutservice",response.ResponseEndpoint);
        Assert.Equal(request.MessageId,response.RequestId);
        Assert.Equal(request.MessageId,response.CausationId);
        Assert.NotEqual(request.MessageId,response.MessageId);
    }

    private sealed class TestCommand:CommandBase;
    private sealed class TestResponse:ResponseBase<TestCommand>;
}
