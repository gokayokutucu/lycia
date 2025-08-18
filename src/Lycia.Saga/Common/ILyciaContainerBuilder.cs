using Autofac;
using Lycia.Saga.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Saga.Common;

public interface ILyciaContainerBuilder
{
    ContainerBuilder Builder { get; }
    System.Configuration.Configuration? Configuration { get; }
    IDictionary<string, (Type MessageType, Type HandlerType)> QueueTypeMap { get; }
}

public class LyciaContainerBuilder : ILyciaContainerBuilder
{
    /// <summary>
    /// Default const
    /// </summary>
    /// <param name="builder">Service collection on application</param>
    /// <param name="configuration">Configuration on application</param>
    /// <param name="queueTypeMap">Query type map on assembly</param>
    public LyciaContainerBuilder(ContainerBuilder builder, System.Configuration.Configuration? configuration, IDictionary<string, (Type MessageType, Type HandlerType)>? queueTypeMap = null)
    {
        Builder = builder;
        Configuration = configuration;
        QueueTypeMap = queueTypeMap ?? new Dictionary<string, (Type MessageType, Type HandlerType)>();
    }
    public LyciaContainerBuilder(ContainerBuilder builder, LyciaOptions options, IDictionary<string, (Type MessageType, Type HandlerType)>? queueTypeMap = null)
    {
        Builder = builder;
        Options = options;
        QueueTypeMap = queueTypeMap ?? new Dictionary<string, (Type MessageType, Type HandlerType)>();
    }
    public LyciaContainerBuilder(ContainerBuilder builder, IDictionary<string, (Type MessageType, Type HandlerType)>? queueTypeMap = null)
    {
        Builder = builder;
        QueueTypeMap = queueTypeMap ?? new Dictionary<string, (Type MessageType, Type HandlerType)>();
    }

    public LyciaOptions Options { get; set; }
    public IDictionary<string, (Type MessageType, Type HandlerType)> QueueTypeMap { get; }
    /// <summary>
    /// Service collection of the app
    /// </summary>
    public ContainerBuilder Builder { get; set; }
    /// <summary>
    /// Configurations of the app
    /// </summary>
    public System.Configuration.Configuration? Configuration { get; set; }
}