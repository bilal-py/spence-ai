using System.Threading.Tasks;
using SpenceAI.Application.Common.Interfaces;

namespace SpenceAI.Infrastructure.Services
{
    public class SyncEngineService : ISyncEngineService
    {
        public Task ExecuteManualSyncAsync(string cloudConnectionString)
        {
            // TODO: Implement actual sync logic
            // For now, we'll just return a completed task.
            return Task.CompletedTask;
        }
    }
}