using System.Security.Cryptography;
using System.Text;
using Testcontainers.RabbitMq;

namespace ParcelFlow.Testing;

// Shared by every integration test project that hosts the Shipment booking saga: its payment
// timeout uses RabbitMQ delayed delivery, and the stock image does not ship this plugin.
internal static class RabbitMqDelayedExchange
{
    private const string PluginFile = "rabbitmq_delayed_message_exchange-3.13.0.ez";
    private const string PluginUrl =
        "https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v3.13.0/" + PluginFile;
    private const string PluginSha256 = "3479180df03fa830fd59ccf638a60eee8f0e8261524a8616685522d7b8d9a9ee";
    private const string EnabledPlugins = "[rabbitmq_management,rabbitmq_prometheus,rabbitmq_delayed_message_exchange].";

    private static readonly Lazy<Task<byte[]>> Plugin = new(LoadPlugin, LazyThreadSafetyMode.ExecutionAndPublication);

    public static async Task<RabbitMqContainer> BuildAsync(Func<RabbitMqBuilder, RabbitMqBuilder> configure)
    {
        var plugin = await Plugin.Value;
        return configure(new RabbitMqBuilder("rabbitmq:3.13-management-alpine"))
            .WithResourceMapping(plugin, $"/opt/rabbitmq/plugins/{PluginFile}")
            .WithResourceMapping(Encoding.ASCII.GetBytes(EnabledPlugins), "/etc/rabbitmq/enabled_plugins")
            .Build();
    }

    private static async Task<byte[]> LoadPlugin()
    {
        // A machine-wide cache avoids one download per test project and per run.
        var cache = Path.Combine(Path.GetTempPath(), "parcelflow-tests", PluginFile);
        if (File.Exists(cache))
        {
            var cached = await File.ReadAllBytesAsync(cache);
            if (IsTrusted(cached)) return cached;
        }

        byte[] plugin = [];
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(1) };
                plugin = await client.GetByteArrayAsync(new Uri(PluginUrl));
                break;
            }
            catch (Exception exception) when (attempt < 3 && exception is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }
        // Pin the archive: an unexpected binary must never be loaded into the broker.
        if (!IsTrusted(plugin))
            throw new InvalidOperationException("Delayed message exchange plugin checksum mismatch.");

        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        var temporary = $"{cache}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporary, plugin);
        try { File.Move(temporary, cache, overwrite: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another test process won the race; its copy is identical.
            File.Delete(temporary);
        }
        return plugin;
    }

    private static bool IsTrusted(byte[] plugin) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(plugin)), PluginSha256, StringComparison.OrdinalIgnoreCase);
}
