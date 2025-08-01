namespace Lycia.Messaging;

public interface IResponse<TPrevious> : IMessage where TPrevious : IMessage{};

public interface ISuccessResponse<TPrevious> : IResponse<TPrevious> where TPrevious : IMessage {}

public interface IFailResponse<TPrevious> : IResponse<TPrevious> where TPrevious : IMessage { }

/// <summary>
/// Handles a successful saga response message that confirms a previous command.
/// </summary>
public interface ISuccessResponseHandler<in TResponse>
{
    Task HandleSuccessResponseAsync(TResponse response);
}

/// <summary>
/// Handles a failed saga response message that indicates a previous command did not complete successfully.
/// </summary>
public interface IFailResponseHandler<in TResponse>
{
    Task HandleFailResponseAsync(TResponse response, FailResponse fail);
}