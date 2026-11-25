using System;

namespace LumenPlayer;

internal sealed record PlaybackBookmark(long Position, string? SubtitleMode);

internal sealed class ResumePosition
{
    public long Position { get; private set; }
    private long _pendingUntil;

    public void Restore(PlaybackBookmark? bookmark, long now)
    {
        Position = Math.Max(0, bookmark?.Position ?? 0);
        _pendingUntil = Position > 0 ? now + 3500 : 0;
    }

    public void Seek(long position, long now)
    {
        Position = position;
        _pendingUntil = now + 1500;
    }

    public void Observe(long position, long now)
    {
        if (position < 0 || (now < _pendingUntil && Math.Abs(Position - position) > 750))
            return;
        _pendingUntil = 0;
        Position = position;
    }

    public PlaybackBookmark? Bookmark(PlaybackRequest? request, bool ended, string? subtitleMode) =>
        request is { Played: true, Failed: false } ? new(ended ? 0 : Position, subtitleMode) : null;
}
