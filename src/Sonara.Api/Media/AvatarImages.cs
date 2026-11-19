using System.Buffers.Binary;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;

namespace Sonara.Api.Media;

public sealed class AvatarImages(IOptions<SonaraOptions> options) : IDisposable
{
    private readonly SemaphoreSlim slots = new(2);
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Assets", "avatars");
    public static string[] Builtins() => Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.png").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray() : [];
    public static string? BuiltinPath(string id) => Builtins().Contains(id, StringComparer.Ordinal) ? Path.Combine(Folder, id + ".png") : null;
    public static bool ValidDimensions(byte[] source)
    {
        int w = 0, h = 0;
        if (source.Length > 24 && source.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
        { w = BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(16,4)); h = BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(20,4)); }
        else if (source.Length > 4 && source[0] == 255 && source[1] == 216)
        {
            var i = 2;
            while (i + 4 < source.Length)
            {
                if (source[i++] != 255) return false;
                while (i < source.Length && source[i] == 255) i++;
                if (i + 2 >= source.Length) return false;
                var marker = source[i++];
                if (marker is 217 or 218) break;
                var length = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(i,2));
                if (length < 2 || i + length > source.Length) return false;
                if (marker is 192 or 193 or 194)
                { if (length < 8) return false; h = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(i+3,2)); w = BinaryPrimitives.ReadUInt16BigEndian(source.AsSpan(i+5,2)); break; }
                i += length;
            }
        }
        return w is > 0 and <= 4096 && h is > 0 and <= 4096;
    }
    public async Task<byte[]?> Normalize(byte[] source, CancellationToken ct)
    {
        if (!ValidDimensions(source)) return null;
        await slots.WaitAsync(ct);
        try
        {
            var info = new ProcessStartInfo(options.Value.Transcoding.FfmpegPath) { UseShellExecute=false, RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true, CreateNoWindow=true };
            foreach(var arg in new[] {"-nostdin", "-v", "error", "-protocol_whitelist", "pipe", "-max_alloc", "67108864", "-f", "image2pipe", "-i", "pipe:0", "-vf", "scale=128:128:force_original_aspect_ratio=decrease,pad=128:128:(ow-iw)/2:(oh-ih)/2:color=black", "-frames:v", "1", "-c:v", "png", "-f", "image2pipe", "pipe:1"}) info.ArgumentList.Add(arg);
            using var process = new Process { StartInfo=info }; process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var kill = timeout.Token.Register(() => {try {if(!process.HasExited) process.Kill(true);} catch(InvalidOperationException) {} });
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            using var output = new MemoryStream(); var read = process.StandardOutput.BaseStream.CopyToAsync(output,timeout.Token);
            try { await process.StandardInput.BaseStream.WriteAsync(source,timeout.Token); } catch(IOException) {} finally { process.StandardInput.Close(); }
            await process.WaitForExitAsync(timeout.Token); await read; await errors;
            return process.ExitCode == 0 && output.Length is > 0 and <= 262144 ? output.ToArray() : null;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        finally { slots.Release(); }
    }
    public void Dispose() => slots.Dispose();
}
