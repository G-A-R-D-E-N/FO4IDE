namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

public sealed record ParallelWriteParameters
{
    public static readonly ParallelWriteParameters Default = new();

    public TaskScheduler? TaskScheduler { get; init; }

    public int MaxDegreeOfParallelism { get; init; } = -1;

    public ushort CutCount { get; init; } = 100;

    public ParallelOptions ParallelOptions => new()
    {
        TaskScheduler = TaskScheduler,
        MaxDegreeOfParallelism = MaxDegreeOfParallelism,
    };
}