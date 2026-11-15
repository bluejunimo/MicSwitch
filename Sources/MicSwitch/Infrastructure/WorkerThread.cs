using System.Collections.Concurrent;

namespace MicSwitch.Infrastructure;

/// <summary>Runs actions one at a time, in order. Everything that touches audio devices goes through one of these.</summary>
internal interface IWorkQueue
{
    void Post(Action action);

    /// <summary>Runs <paramref name="action"/> on the queue after <paramref name="delay"/>. Dispose to cancel.</summary>
    IDisposable Schedule(TimeSpan delay, Action action);
}

internal sealed class WorkerThread : IWorkQueue, IDisposable
{
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread thread;

    public WorkerThread(string name)
    {
        thread = new Thread(Run) { IsBackground = true, Name = name };
        // Core Audio objects are free-threaded; an MTA thread avoids cross-apartment marshalling.
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public void Post(Action action)
    {
        try
        {
            queue.Add(action);
        }
        catch (InvalidOperationException)
        {
            // Shutting down.
        }
    }

    public Task InvokeAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    public IDisposable Schedule(TimeSpan delay, Action action) => new ScheduledAction(this, delay, action);

    private void Run()
    {
        foreach (var action in queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled error on {Thread}", thread.Name);
            }
        }
    }

    public void Dispose()
    {
        queue.CompleteAdding();
        if (Thread.CurrentThread != thread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class ScheduledAction : IDisposable
    {
        private readonly Timer timer;
        private volatile bool cancelled;

        public ScheduledAction(WorkerThread owner, TimeSpan delay, Action action)
        {
            timer = new Timer(_ =>
            {
                if (!cancelled)
                {
                    owner.Post(() =>
                    {
                        if (!cancelled)
                        {
                            action();
                        }
                    });
                }
            }, null, delay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            cancelled = true;
            timer.Dispose();
        }
    }
}
