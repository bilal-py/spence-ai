using System.Threading.Tasks;

namespace SpenceAI.Application.Common.Interfaces
{
    public interface ISyncEngineService
    {
        Task ExecuteManualSyncAsync(string cloudConnectionString);
    }
}