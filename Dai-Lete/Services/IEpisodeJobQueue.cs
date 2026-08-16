using Dai_Lete.Models;

namespace Dai_Lete.Services;

public interface IEpisodeJobQueue
{
    Task<string> EnqueueAsync(EpisodeJob job);
    Task<IReadOnlyList<QueuedEpisodeJob>> ReadAsync(CancellationToken cancellationToken);
    Task AckAsync(string jobId);
    Task CompleteAsync(QueuedEpisodeJob queuedJob);
}
