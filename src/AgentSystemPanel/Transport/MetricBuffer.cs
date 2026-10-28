using System.Collections.Concurrent;
using System.Text.Json;
using SystemPanel.Contracts.Agents;

namespace AgentSystemPanel.Transport;

/// <summary>
/// Sunucuya ulaşılamazken ölçümleri sırayla tutan sınırlı tampon. Doluysa EN ESKİ ölçüm atılır.
/// Servis dururken diske yazılır, açılışta geri yüklenir (yeniden başlatmada veri kaybını azaltır).
/// </summary>
public sealed class MetricBuffer
{
    private const string FileName = "pending-metrics.json";
    private readonly ConcurrentQueue<AgentMetricsRequest> _queue = new();
    private readonly int _capacity;
    private int _dropped;

    public MetricBuffer(int capacity) => _capacity = Math.Max(1, capacity);

    public int Count => _queue.Count;

    /// <summary>Kapasite aşımı nedeniyle atılan ölçüm sayısı (başlangıçtan beri).</summary>
    public int Dropped => _dropped;

    public void Enqueue(AgentMetricsRequest item)
    {
        _queue.Enqueue(item);
        while (_queue.Count > _capacity && _queue.TryDequeue(out _))
            Interlocked.Increment(ref _dropped);
    }

    public bool TryPeek(out AgentMetricsRequest? item) => _queue.TryPeek(out item);

    public bool TryDequeue(out AgentMetricsRequest? item) => _queue.TryDequeue(out item);

    public void SaveTo(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var items = _queue.ToArray();

        if (items.Length == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(items));
        File.Move(tmp, path, overwrite: true);
    }

    public int LoadFrom(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path)) return 0;

        try
        {
            var items = JsonSerializer.Deserialize<List<AgentMetricsRequest>>(File.ReadAllText(path)) ?? new();
            foreach (var item in items) Enqueue(item);
            File.Delete(path);
            return items.Count;
        }
        catch (JsonException)
        {
            // Bozuk tampon dosyası: kenara al, agent'ı durdurma.
            File.Move(path, path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), overwrite: true);
            return 0;
        }
    }
}
