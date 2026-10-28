namespace Clothesline.Core;

/// <summary>
/// Spots a card being shaken while it is dragged: <see cref="Turns"/> quick changes of direction, each at least
/// <see cref="MinSwing"/> DIPs from the last turning point, all within <see cref="WindowMs"/>.
/// </summary>
internal sealed class Shake
{
    public const int Turns = 4, WindowMs = 800;
    public const double MinSwing = 24;

    private readonly Queue<long> _turns = new();
    private double _extreme = double.NaN;
    private int _dir;

    public void Reset()
    {
        _turns.Clear();
        _extreme = double.NaN;
        _dir = 0;
    }

    /// <summary>Feeds the cursor's x (DIPs) at <paramref name="ms"/>; true once, when the shake is complete.</summary>
    public bool Add(double x, long ms)
    {
        if (double.IsNaN(_extreme)) { _extreme = x; return false; }
        int dir = Math.Sign(x - _extreme);
        if (dir == 0) return false;
        if (dir == _dir) { _extreme = x; return false; } // still going the same way: the turning point moves along
        if (Math.Abs(x - _extreme) < MinSwing) return false;
        if (_dir != 0) _turns.Enqueue(ms); // the first swing only sets the direction
        _dir = dir;
        _extreme = x;
        while (_turns.Count > 0 && _turns.Peek() < ms - WindowMs) _turns.Dequeue();
        if (_turns.Count < Turns) return false;
        Reset();
        return true;
    }
}
