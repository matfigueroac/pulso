using System;
using System.Collections.ObjectModel;

namespace LumenPlayer;

internal enum RepeatMode { Off, All, Current }

internal sealed class PlaybackQueue<T> where T : class
{
    public ObservableCollection<T> Items { get; } = [];
    public T? Current { get; private set; }
    public RepeatMode Repeat { get; private set; }
    public PlaybackRequest? Request { get; private set; }

    public PlaybackRequest Begin(T entry)
    {
        Current = entry;
        return Request = new PlaybackRequest();
    }

    public void Stop() => Request = null;

    public bool CanStart(PlaybackRequest request) => ReferenceEquals(Request, request) && !request.Started;

    public void CycleRepeat() => Repeat = Repeat switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.Current,
        _ => RepeatMode.Off
    };

    public T? Relative(int offset)
    {
        if (Items.Count == 0)
            return null;
        int index = Current is null ? 0 : Items.IndexOf(Current) + offset;
        if (Repeat == RepeatMode.All)
            index = (index + Items.Count) % Items.Count;
        return index >= 0 && index < Items.Count ? Items[index] : null;
    }

    public T? AfterEnd() => Repeat == RepeatMode.Current ? Current : Relative(1);

    public (bool RemovedCurrent, T? Replacement, int Selection) Remove(T entry)
    {
        int index = Items.IndexOf(entry);
        if (index < 0)
            return (false, null, -1);
        Items.RemoveAt(index);
        int selection = Math.Min(index, Items.Count - 1);
        if (!ReferenceEquals(Current, entry))
            return (false, null, selection);

        var replacement = Request is not null && selection >= 0 ? Items[selection] : null;
        Stop();
        Current = null;
        return (true, replacement, selection);
    }
}

internal sealed class PlaybackRequest
{
    public bool Started { get; set; }
    public volatile bool Played;
    public volatile bool Failed;
    public void OnPlaying(object? sender, EventArgs e) => Played = true;
    public void OnError(object? sender, EventArgs e) => Failed = true;
}
