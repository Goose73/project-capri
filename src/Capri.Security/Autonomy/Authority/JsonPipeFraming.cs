using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Capri.Security.Autonomy.Authority;

internal static class JsonPipeFraming
{
    public static async Task WriteAsync<T>(Stream stream, T value, JsonSerializerOptions options, int maxBytes, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, options);
        if (json.Length <= 0 || json.Length > maxBytes)
            throw new InvalidOperationException($"IPC frame size {json.Length} is invalid.");
        var len = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, json.Length);
        await stream.WriteAsync(len, ct).ConfigureAwait(false);
        await stream.WriteAsync(json, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, JsonSerializerOptions options, int maxBytes, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        if (!await ReadExactAsync(stream, lenBuf, ct).ConfigureAwait(false)) return default;
        var len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (len <= 0 || len > maxBytes) throw new InvalidOperationException("Invalid IPC frame length.");
        var buf = new byte[len];
        if (!await ReadExactAsync(stream, buf, ct).ConfigureAwait(false)) return default;
        return JsonSerializer.Deserialize<T>(buf, options);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0) return offset == 0 ? false : throw new EndOfStreamException("Incomplete IPC frame.");
            offset += read;
        }
        return true;
    }
}
