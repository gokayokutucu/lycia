// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lycia.Common;

/// <summary>Provides the services, configuration, and discovered message routing map used while configuring Lycia.</summary>
public interface ILyciaServiceCollection
{
    /// <summary>Gets the application service collection being configured.</summary>
    IServiceCollection Services { get; }
    /// <summary>Gets the application configuration when one was supplied.</summary>
    IConfiguration? Configuration { get; }
    /// <summary>Gets the topology map from transport queue names to message and handler types.</summary>
    IDictionary<string, (Type MessageType, Type HandlerType)> QueueTypeMap { get; }
}

/// <summary>Default mutable configuration context returned by Lycia service-registration extensions.</summary>
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
    
    
    /// <inheritdoc />
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
