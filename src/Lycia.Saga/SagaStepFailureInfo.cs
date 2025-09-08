// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System;

namespace Lycia.Saga;

public class SagaStepFailureInfo
{
    public SagaStepFailureInfo(string? reason, string? exceptionType, string? exceptionDetail)
    {
        Reason = reason;
        ExceptionType = exceptionType;
        ExceptionDetail = exceptionDetail;
    }

    public string? Reason { get; }
    public string? ExceptionType { get; }
    public string? ExceptionDetail { get; }

    public override bool Equals(object? obj)
    {
        return obj is SagaStepFailureInfo other &&
               Reason == other.Reason &&
               ExceptionType == other.ExceptionType &&
               ExceptionDetail == other.ExceptionDetail;
    }
    
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + (Reason?.GetHashCode() ?? 0);
            hash = hash * 23 + (ExceptionType?.GetHashCode() ?? 0);
            hash = hash * 23 + (ExceptionDetail?.GetHashCode() ?? 0);
            return hash;
        }
    }
    
    public override string ToString()
    {
        return $"Reason: {Reason} \n ExceptionType: {ExceptionType} \n ExceptionDetail: {ExceptionDetail}\n";
    }
}