using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FO4RecordEditor.Services;

















public static class AudioService
{
    private static bool IsExt(string path, string ext) =>
        string.Equals(Path.GetExtension(path), ext, StringComparison.OrdinalIgnoreCase);



    private static readonly HashSet<string> BatchExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3", ".flac", ".ogg", ".oga", ".m4a", ".wma", ".aif", ".aiff", ".mp4", ".avi" };








    public static string ConvertToXwm(string source, string? output, int? bitrateBps)
    {
        if (string.IsNullOrWhiteSpace(source)) return ToolError.Fail("Provide 'source' (an audio/video file, or a folder of them).");
        source = source.Trim().Trim('"');

        if (Directory.Exists(source)) return ConvertFolderToXwm(source, output, bitrateBps);
        if (!File.Exists(source)) return ToolError.Fail($"Source not found: {source}");
        return ConvertOneToXwm(source, output, bitrateBps);
    }



    private static string ConvertFolderToXwm(string sourceDir, string? outputDir, int? bitrateBps)
    {
        var files = Directory.EnumerateFiles(sourceDir, "*.*", SearchOption.AllDirectories)
            .Where(f => BatchExtensions.Contains(Path.GetExtension(f)))
            .ToList();
        if (files.Count == 0) return ToolError.Fail($"No audio/video files found under '{sourceDir}' (looked for {string.Join(", ", BatchExtensions)}).");

        outputDir = string.IsNullOrWhiteSpace(outputDir) ? null : outputDir.Trim().Trim('"');
        var ok = 0;
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        var degree = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));

        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = degree }, file =>
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var target = outputDir == null
                ? Path.ChangeExtension(file, ".xwm")
                : Path.ChangeExtension(Path.Combine(outputDir, rel), ".xwm");

            var result = ConvertOneToXwm(file, target, bitrateBps);
            if (result.StartsWith("RESULT: success", StringComparison.Ordinal)) Interlocked.Increment(ref ok);
            else
            {


                var clean = ToolError.IsMarked(result) ? result[1..] : result;
                failures.Add($"{rel}: {clean.Split('\n')[0]}");
            }
        });

        var msg = $"RESULT: {ok}/{files.Count} converted" +
                  (outputDir != null ? $" (output -> {outputDir})" : " (in place, alongside each source)") + ".";
        if (!failures.IsEmpty)
            msg += $"\n{failures.Count} FAILED:\n  " + string.Join("\n  ", failures.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(20));
        return msg;
    }






    public static string ConvertOneToXwm(string source, string? output, int? bitrateBps)
    {
        if (string.IsNullOrWhiteSpace(source)) return ToolError.Fail("Provide 'source' (an audio or video file).");
        source = source.Trim().Trim('"');
        if (!File.Exists(source)) return ToolError.Fail($"Source not found: {source}");

        var xwmaEncode = ToolPaths.XwmaEncode();
        if (xwmaEncode == null)
            return ToolError.Fail("xWMAEncode not found. " + ToolPaths.Describe("xwmaencode") + ".");

        var outPath = string.IsNullOrWhiteSpace(output)
            ? Path.ChangeExtension(source, ".xwm")
            : output.Trim().Trim('"');
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".") ; }
        catch (Exception ex) { return ToolError.Fail($"Cannot create output dir for '{outPath}': {ex.Message}"); }

        string? tempWav = null;
        try
        {
            var wavIn = source;
            if (!IsPcmWav(source))
            {
                var normalized = NormalizeToPcmWav(source);
                if (normalized.error != null) return normalized.error;
                tempWav = normalized.wavPath;
                wavIn = tempWav!;
            }

            var args = new List<string>();
            if (bitrateBps is int b) { args.Add("-b"); args.Add(b.ToString()); }
            args.Add(wavIn);
            args.Add(outPath);

            var run = RunTool(xwmaEncode, args, TimeSpan.FromSeconds(60));
            if (!run.Started) return ToolError.Fail("Failed to start xWMAEncode.");
            if (run.TimedOut) return ToolError.Fail("xWMAEncode timed out after 60s (killed).");
            if (run.ExitCode != 0 || !File.Exists(outPath))
                return ToolError.Fail($"xWMAEncode failed (exit {run.ExitCode}):\n{run.Combined}");

            return $"RESULT: success (output -> {outPath})\n\n{run.Combined}".TrimEnd();
        }
        finally { if (tempWav != null) TryDelete(tempWav); }
    }





    public static string ConvertFromXwm(string source, string? output, string? targetExt)
    {
        if (string.IsNullOrWhiteSpace(source)) return ToolError.Fail("Provide 'source' (a .xwm file).");
        source = source.Trim().Trim('"');
        if (!File.Exists(source)) return ToolError.Fail($"Source not found: {source}");

        var xwmaEncode = ToolPaths.XwmaEncode();
        if (xwmaEncode == null)
            return ToolError.Fail("xWMAEncode not found. " + ToolPaths.Describe("xwmaencode") + ".");

        targetExt = string.IsNullOrWhiteSpace(targetExt) ? "wav" : targetExt.Trim().TrimStart('.').ToLowerInvariant();
        var finalOut = string.IsNullOrWhiteSpace(output)
            ? Path.ChangeExtension(source, "." + targetExt)
            : output.Trim().Trim('"');
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(finalOut)) ?? "."); }
        catch (Exception ex) { return ToolError.Fail($"Cannot create output dir for '{finalOut}': {ex.Message}"); }



        var wantsWav = targetExt == "wav";
        var wavOut = wantsWav ? finalOut : Path.Combine(Path.GetTempPath(),
            "FO4RE_Audio_" + Guid.NewGuid().ToString("N").Substring(0, 10) + ".wav");
        try
        {
            var run = RunTool(xwmaEncode, new List<string> { source, wavOut }, TimeSpan.FromSeconds(60));
            if (!run.Started) return ToolError.Fail("Failed to start xWMAEncode.");
            if (run.TimedOut) return ToolError.Fail("xWMAEncode timed out after 60s (killed).");
            if (run.ExitCode != 0 || !File.Exists(wavOut))
                return ToolError.Fail($"xWMAEncode failed (exit {run.ExitCode}):\n{run.Combined}");

            if (wantsWav) return $"RESULT: success (output -> {finalOut})\n\n{run.Combined}".TrimEnd();

            var ff = ToolPaths.Ffmpeg();
            if (ff == null) return ToolError.Fail("ffmpeg not found. " + ToolPaths.Describe("ffmpeg") + $". (Decoded WAV is at {wavOut}.)");

            var ffArgs = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-i", wavOut, finalOut };
            var ffRun = RunTool(ff, ffArgs, TimeSpan.FromSeconds(60));
            if (!ffRun.Started) return ToolError.Fail("Failed to start ffmpeg.");
            if (ffRun.TimedOut) return ToolError.Fail("ffmpeg timed out after 60s (killed).");
            if (ffRun.ExitCode != 0 || !File.Exists(finalOut))
                return ToolError.Fail($"ffmpeg failed (exit {ffRun.ExitCode}):\n{ffRun.Combined}");

            return $"RESULT: success (output -> {finalOut})".TrimEnd();
        }
        finally { if (!wantsWav) TryDelete(wavOut); }
    }






    public static string MakeFuz(string audioSource, string? lipPath, string fuzOutput, bool noLip)
    {
        if (string.IsNullOrWhiteSpace(audioSource)) return ToolError.Fail("Provide 'audio_source'.");
        audioSource = audioSource.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(fuzOutput)) return ToolError.Fail("Provide 'fuz_output'.");
        fuzOutput = fuzOutput.Trim().Trim('"');
        if (!File.Exists(audioSource)) return ToolError.Fail($"Source not found: {audioSource}");

        var fuzEncode = ToolPaths.BmlFuzEncode();
        if (fuzEncode == null) return ToolError.Fail("BmlFuzEncode not found. " + ToolPaths.Describe("bmlfuzencode") + ".");

        lipPath = string.IsNullOrWhiteSpace(lipPath) ? Path.ChangeExtension(audioSource, ".lip") : lipPath.Trim().Trim('"');
        var haveLip = !noLip && File.Exists(lipPath);

        string? tempXwm = null;
        try
        {
            var xwmPath = audioSource;
            if (!IsExt(audioSource, ".xwm"))
            {
                tempXwm = Path.Combine(Path.GetTempPath(), "FO4RE_Audio_" + Guid.NewGuid().ToString("N").Substring(0, 10) + ".xwm");
                var enc = ConvertOneToXwm(audioSource, tempXwm, null);
                if (!enc.StartsWith("RESULT: success", StringComparison.Ordinal)) return ToolError.Fail("Encoding to xwm failed:\n" + enc);
                xwmPath = tempXwm;
            }

            try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fuzOutput)) ?? "."); }
            catch (Exception ex) { return ToolError.Fail($"Cannot create output dir for '{fuzOutput}': {ex.Message}"); }

            var args = new List<string>();
            if (noLip) args.Add("-nolip");
            args.Add(fuzOutput);
            args.Add(xwmPath);
            if (haveLip) args.Add(lipPath);

            var run = RunTool(fuzEncode, args, TimeSpan.FromSeconds(30));
            if (!run.Started) return ToolError.Fail("Failed to start BmlFuzEncode.");
            if (run.TimedOut) return ToolError.Fail("BmlFuzEncode timed out after 30s (killed).");
            if (run.ExitCode != 0 || !File.Exists(fuzOutput))
                return ToolError.Fail($"BmlFuzEncode failed (exit {run.ExitCode}):\n{run.Combined}");

            return $"RESULT: success (output -> {fuzOutput}, lip {(haveLip ? "included" : "not included")})\n\n{run.Combined}".TrimEnd();
        }
        finally { if (tempXwm != null) TryDelete(tempXwm); }
    }



    public static string ExtractFuz(string fuzPath, string? xwmOutput, string? lipOutput, bool alsoWav)
    {
        if (string.IsNullOrWhiteSpace(fuzPath)) return ToolError.Fail("Provide 'fuz_path'.");
        fuzPath = fuzPath.Trim().Trim('"');
        if (!File.Exists(fuzPath)) return ToolError.Fail($"Source not found: {fuzPath}");

        var fuzDecode = ToolPaths.BmlFuzDecode();
        if (fuzDecode == null) return ToolError.Fail("BmlFuzDecode not found. " + ToolPaths.Describe("bmlfuzdecode") + ".");

        var xwmOut = string.IsNullOrWhiteSpace(xwmOutput) ? Path.ChangeExtension(fuzPath, ".xwm") : xwmOutput.Trim().Trim('"');
        var lipOut = string.IsNullOrWhiteSpace(lipOutput) ? Path.ChangeExtension(fuzPath, ".lip") : lipOutput.Trim().Trim('"');

        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(xwmOut)) ?? "."); }
        catch (Exception ex) { return ToolError.Fail($"Cannot create output dir for '{xwmOut}': {ex.Message}"); }

        var run = RunTool(fuzDecode, new List<string> { fuzPath, xwmOut, lipOut }, TimeSpan.FromSeconds(30));
        if (!run.Started) return ToolError.Fail("Failed to start BmlFuzDecode.");
        if (run.TimedOut) return ToolError.Fail("BmlFuzDecode timed out after 30s (killed).");
        if (run.ExitCode != 0 || !File.Exists(xwmOut))
            return ToolError.Fail($"BmlFuzDecode failed (exit {run.ExitCode}):\n{run.Combined}");

        var msg = new StringBuilder($"RESULT: success (xwm -> {xwmOut}");
        var haveLip = File.Exists(lipOut);
        msg.Append(haveLip ? $", lip -> {lipOut})" : ", no lip in this fuz)");

        if (alsoWav)
        {
            var wavOut = Path.ChangeExtension(xwmOut, ".wav");
            var dec = ConvertFromXwm(xwmOut, wavOut, "wav");
            msg.Append(dec.StartsWith("RESULT: success") ? $"\nwav -> {wavOut}" : $"\nwav decode failed: {dec}");
        }
        return msg.ToString();
    }






    private static bool IsPcmWav(string path)
    {
        if (!IsExt(path, ".wav")) return false;
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> riff = stackalloc byte[12];
            if (fs.Read(riff) < 12) return false;
            if (BitConverter.ToUInt32(riff.Slice(0, 4)) != 0x46464952u) return false;
            if (BitConverter.ToUInt32(riff.Slice(8, 4)) != 0x45564157u) return false;

            Span<byte> chunkHeader = stackalloc byte[8];
            while (fs.Read(chunkHeader) == 8)
            {
                var id = BitConverter.ToUInt32(chunkHeader.Slice(0, 4));
                var size = BitConverter.ToUInt32(chunkHeader.Slice(4, 4));
                if (id == 0x20746d66u)
                {
                    Span<byte> fmt = stackalloc byte[2];
                    if (fs.Read(fmt) < 2) return false;
                    var tag = BitConverter.ToUInt16(fmt);
                    return tag == 1 || tag == 0xFFFE;
                }
                fs.Seek(size + (size % 2), SeekOrigin.Current);
            }
        }
        catch {  }
        return false;
    }


    private static (string? wavPath, string? error) NormalizeToPcmWav(string source)
    {
        var ff = ToolPaths.Ffmpeg();
        if (ff == null) return (null, ToolError.Fail("ffmpeg not found. " + ToolPaths.Describe("ffmpeg") + "."));

        var wav = Path.Combine(Path.GetTempPath(), "FO4RE_Audio_" + Guid.NewGuid().ToString("N").Substring(0, 10) + ".wav");
        var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-y", "-i", source, "-vn", "-acodec", "pcm_s16le", wav };
        var run = RunTool(ff, args, TimeSpan.FromSeconds(60));
        if (!run.Started) return (null, ToolError.Fail("Failed to start ffmpeg."));
        if (run.TimedOut) return (null, ToolError.Fail("ffmpeg timed out after 60s (killed)."));
        if (run.ExitCode != 0 || !File.Exists(wav))
            return (null, ToolError.Fail($"ffmpeg failed to decode '{source}' (exit {run.ExitCode}):\n{run.Combined}"));
        return (wav, null);
    }

    private static ProcessResult RunTool(string exe, List<string> args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo { FileName = exe };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return ProcessRunner.Run(psi, timeout);
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
}
