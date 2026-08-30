namespace BitMagic.X16Debugger.LSP;

public sealed class Debouncer : IDisposable
{
    private readonly int _delayMilliseconds;
    private readonly Timer _timer;
    private Func<Task>? _action = null;
    private readonly object _lock = new();

    public Debouncer(int delayMilliseconds = 1000)
    {
        _delayMilliseconds = delayMilliseconds;
        _timer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Debounce(Func<Task> action)
    {
        lock (_lock)
        {
            _action = action;
            _timer.Change(_delayMilliseconds, Timeout.Infinite);
        }
    }

    private async void OnTimerElapsed(object? state)
    {
        Func<Task>? actionToRun;

        lock (_lock)
        {
            actionToRun = _action;
            _action = null;
        }

        if (actionToRun == null)
            return;

        // Observe the task. Previously the returned Task was dropped, so an exception
        // escaping the action was unobserved and the debounced pipeline died silently.
        // The action (UpdateFileChanges) owns fault reporting; this is last-resort.
        try
        {
            await actionToRun();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[X16D] Debounced action faulted: {ex}");
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
