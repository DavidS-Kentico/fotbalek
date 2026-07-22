using System.Collections.Concurrent;

namespace Fotbalek.Web.Realtime;

/// <summary>
/// In-memory tracker of which users currently have at least one live Blazor Server circuit.
/// State is ephemeral by design — restarting the server resets presence.
/// </summary>
public class PresenceTracker
{
    private readonly ConcurrentDictionary<int, int> _circuitsByUser = new();

    public event Action? Changed;

    public void Track(int userId)
    {
        _circuitsByUser.AddOrUpdate(userId, 1, (_, count) => count + 1);
        Changed?.Invoke();
    }

    public void Untrack(int userId)
    {
        var removed = false;
        _circuitsByUser.AddOrUpdate(
            userId,
            _ => 0,
            (_, count) =>
            {
                var next = count - 1;
                if (next <= 0)
                {
                    removed = true;
                    return 0;
                }
                return next;
            });

        if (removed)
        {
            _circuitsByUser.TryRemove(new KeyValuePair<int, int>(userId, 0));
        }
        Changed?.Invoke();
    }

    public bool IsOnline(int userId) => _circuitsByUser.TryGetValue(userId, out var count) && count > 0;

    public int OnlineCount => _circuitsByUser.Count(kv => kv.Value > 0);
}
