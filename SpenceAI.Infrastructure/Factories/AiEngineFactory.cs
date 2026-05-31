using System;
using SpenceAI.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using SpenceAI.Infrastructure.Services;

namespace SpenceAI.Infrastructure.Factories;

public class AiEngineFactory : IAiEngineFactory
{
    private readonly IServiceProvider _serviceProvider;

    public AiEngineFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public IAiEngineService GetEngine(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name is required", nameof(providerName));

        return providerName.ToLower() switch
        {
            "gemini" => (IAiEngineService)_serviceProvider.GetRequiredService(typeof(GeminiService)),
            _ => throw new NotSupportedException($"AI Provider '{providerName}' is not supported.")
        };
    }
}
