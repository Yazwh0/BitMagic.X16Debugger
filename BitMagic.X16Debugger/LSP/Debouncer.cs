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

    private void OnTimerElapsed(object? state)
    {
        Func<Task>? actionToRun;

        lock (_lock)
        {
            actionToRun = _action;
            _action = null;
        }

        actionToRun?.Invoke();
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
