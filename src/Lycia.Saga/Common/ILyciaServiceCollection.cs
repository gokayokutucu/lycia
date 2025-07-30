using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Saga.Common;

public interface ILyciaServiceCollection
{
    IServiceCollection Services { get; }
    IConfiguration? Configuration { get; }
    IDictionary<string, (Type MessageType, Type HandlerType)> QueueTypeMap { get; }
}

public class LyciaServiceCollection : ILyciaServiceCollection
{
    /// <summary>
    /// Default const
    /// </summary>
    /// <param name="services">Service collection on application</param>
    /// <param name="configuration">Configuration on application</param>
    /// <param name="queueTypeMap">Query type map on assembly</param>
    public LyciaServiceCollection(IServiceCollection services, IConfiguration? configuration, IDictionary<string, (Type MessageType, Type HandlerType)>? queueTypeMap = null)
    {
        Services = services;
        Configuration = configuration;
        QueueTypeMap = queueTypeMap ?? new Dictionary<string, (Type MessageType, Type HandlerType)>();
    }
    
    
    public IDictionary<string, (Type MessageType, Type HandlerType)> QueueTypeMap { get; }
    /// <summary>
    /// Service collection of the app
    /// </summary>
    public IServiceCollection Services { get; set; }
    /// <summary>
    /// Configurations of the app
    /// </summary>
    public IConfiguration? Configuration { get; set; }
}