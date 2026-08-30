using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.ReSharper.Feature.Services.Daemon;

namespace RiderBackendEffectSpike;

/// <summary>
/// Stands in for the future rig resident host. A daemon pass can only read the
/// immutable snapshot; a miss schedules work and returns immediately.
/// </summary>
internal sealed class FakeFileEffectHost
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<FileEffectRow>> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();
    private readonly IDaemon _daemon;

    public FakeFileEffectHost(IDaemon daemon)
    {
        _daemon = daemon;
    }

    public bool TryGet(string filePath, out IReadOnlyList<FileEffectRow> rows) =>
        _cache.TryGetValue(filePath, out rows);

    public void Request(string filePath)
    {
        if (!_inFlight.TryAdd(filePath, 0))
            return;

        Console.WriteLine($"[rig-spike] cache miss: {filePath}");
        _ = LoadAsync(filePath);
    }

    private async Task LoadAsync(string filePath)
    {
        try
        {
            await Task.Delay(150).ConfigureAwait(false);

            // Contract fixture: the host returns stable symbol identities, not
            // editor offsets. The backend resolves these against the current PSI.
            _cache[filePath] = new[]
            {
                new FileEffectRow("M:Demo.OrderService.Load", "db.read", 3),
                new FileEffectRow("M:Demo.OrderService.Save(System.Int32)", "db.write", 2),
            };

            Console.WriteLine($"[rig-spike] fake response ready: {filePath}");
            _daemon.Invalidate("rig file-effect response arrived");
        }
        finally
        {
            _inFlight.TryRemove(filePath, out _);
        }
    }
}
