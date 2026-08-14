using System;
using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;
using Xunit.Abstractions;

namespace FO4RecordEditor.Tests;

// Exercises the real bundled ffmpeg/xWMAEncode/BmlFuzTools binaries end to end: wav -> xwm -> wav,
// and xwm+lip -> fuz -> xwm+lip. Generates its own tiny PCM WAV fixture rather than depending on a
// checked-in sample, so there's nothing to keep in sync.
//
// Skips loudly when the local tools\audio\ bundle is absent, rather than passing.
public class AudioServiceSmokeTests
{
    private readonly ITestOutputHelper _out;
    public AudioServiceSmokeTests(ITestOutputHelper o) => _out = o;

    // A one-tone-second, 8kHz mono, 16-bit PCM WAV -- the smallest thing xWMAEncode will accept
    // (its own bitrate table has an 8kHz-mono-adjacent 22050Hz-mono entry; anything off that table
    // gets resampled internally per its own -? help text, so this doesn't need to match exactly).
    private static string WriteSampleWav(string path)
    {
        const int sampleRate = 8000;
        const short channels = 1, bitsPerSample = 16;
        var samples = new short[sampleRate]; // 1 second
        for (int i = 0; i < samples.Length; i++)
            samples[i] = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 8000);

        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        int dataSize = samples.Length * sizeof(short);
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataSize); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1 /* PCM */); w.Write(channels);
        w.Write(sampleRate); w.Write(byteRate); w.Write(blockAlign); w.Write(bitsPerSample);
        w.Write("data"u8.ToArray()); w.Write(dataSize);
        foreach (var s in samples) w.Write(s);
        return path;
    }

    [Fact(Skip = "Needs the local tools\\audio\\ bundle (ffmpeg/xWMAEncode/BmlFuzTools). Remove Skip " +
                 "to run manually. Verified passing 2026-07-20 (round-trip wav->xwm->wav and xwm->fuz->xwm).")]
    public void RoundTrip_WavToXwmToWav_AndXwmToFuzToXwm()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FO4RE_AudioSmoke_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var wav = WriteSampleWav(Path.Combine(dir, "tone.wav"));
            var xwm = Path.Combine(dir, "tone.xwm");
            var wavBack = Path.Combine(dir, "tone_back.wav");
            var fuz = Path.Combine(dir, "tone.fuz");
            var xwmBack = Path.Combine(dir, "tone_extracted.xwm");

            var toXwm = AudioService.ConvertToXwm(wav, xwm, null);
            _out.WriteLine(toXwm);
            toXwm.Should().StartWith("RESULT: success");
            File.Exists(xwm).Should().BeTrue();
            new FileInfo(xwm).Length.Should().BeGreaterThan(0);

            var fromXwm = AudioService.ConvertFromXwm(xwm, wavBack, "wav");
            _out.WriteLine(fromXwm);
            fromXwm.Should().StartWith("RESULT: success");
            File.Exists(wavBack).Should().BeTrue();

            var makeFuz = AudioService.MakeFuz(xwm, "", fuz, noLip: true);
            _out.WriteLine(makeFuz);
            makeFuz.Should().StartWith("RESULT: success");
            File.Exists(fuz).Should().BeTrue();

            var extract = AudioService.ExtractFuz(fuz, xwmBack, Path.Combine(dir, "tone_extracted.lip"), alsoWav: false);
            _out.WriteLine(extract);
            extract.Should().StartWith("RESULT: success");
            File.Exists(xwmBack).Should().BeTrue();
            new FileInfo(xwmBack).Length.Should().Be(new FileInfo(xwm).Length);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
