using Hangfire;

namespace Modules.Content.Jobs;

public sealed class ContentVideoDispatchService(
    IBackgroundJobClient backgroundJobs,
    ILogger<ContentVideoDispatchService> logger)
{
    public bool EnqueuePlan(Guid projectId, Guid videoId) => TryDispatch(
        () => backgroundJobs.Enqueue<ContentVideoJob>(job => job.PlanAsync(projectId, videoId)),
        "plan",
        projectId,
        videoId);

    public bool EnqueueGeneration(Guid projectId, Guid videoId) => TryDispatch(
        () => backgroundJobs.Enqueue<ContentVideoJob>(
            job => job.GenerateNextSceneAsync(projectId, videoId)),
        "generation",
        projectId,
        videoId);

    public bool ScheduleGeneration(Guid projectId, Guid videoId, TimeSpan delay) => TryDispatch(
        () => backgroundJobs.Schedule<ContentVideoJob>(
            job => job.GenerateNextSceneAsync(projectId, videoId),
            delay),
        "generation",
        projectId,
        videoId);

    public bool EnqueueAssembly(Guid projectId, Guid videoId) => TryDispatch(
        () => backgroundJobs.Enqueue<ContentVideoJob>(job => job.AssembleAsync(projectId, videoId)),
        "assembly",
        projectId,
        videoId);

    private bool TryDispatch(
        Func<string> dispatch,
        string operation,
        Guid projectId,
        Guid videoId)
    {
        try
        {
            var jobId = dispatch();
            if (!string.IsNullOrWhiteSpace(jobId)) return true;
            logger.LogError(
                "Content video {Operation} dispatch returned no job id for {ProjectId}/{VideoId}",
                operation,
                projectId,
                videoId);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to dispatch content video {Operation} for {ProjectId}/{VideoId}",
                operation,
                projectId,
                videoId);
            return false;
        }
    }
}
