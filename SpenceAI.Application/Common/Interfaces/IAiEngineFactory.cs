using System;
namespace SpenceAI.Application.Common.Interfaces;

public interface IAiEngineFactory
{
    SpenceAI.Application.Common.Interfaces.IAiEngineService GetEngine(string providerName);
}
