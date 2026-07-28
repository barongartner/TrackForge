using System.Diagnostics;

namespace TrackForge.Core;

public sealed record AnalysisResult(double? Bpm, string? Key, string? Camelot);

/// <summary>
/// BPM and musical key straight off the waveform. ffmpeg decodes to mono float32,
/// then spectral-flux autocorrelation gives tempo and a Krumhansl-Schmuckler
/// chroma correlation gives key. No external audio library.
/// </summary>
public static class AudioAnalyzer
{
    private const int SampleRate = 22050;
    private const int FftSize = 2048;
    private const int HopSize = 512;
    private const int MaxSeconds = 420;

    private static readonly string[] Pitches =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    // Camelot wheel, indexed by pitch class.
    private static readonly string[] CamelotMajor =
        { "8B", "3B", "10B", "5B", "12B", "7B", "2B", "9B", "4B", "11B", "6B", "1B" };
    private static readonly string[] CamelotMinor =
        { "5A", "12A", "7A", "2A", "9A", "4A", "11A", "6A", "1A", "8A", "3A", "10A" };

    // Krumhansl-Kessler key profiles.
    private static readonly double[] MajorProfile =
        { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] MinorProfile =
        { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

    public static async Task<AnalysisResult> AnalyzeAsync(
        string path, string ffmpegPath, CancellationToken ct = default)
    {
        try
        {
            var samples = await DecodeAsync(path, ffmpegPath, ct).ConfigureAwait(false);
            if (samples is null || samples.Length < SampleRate) return new AnalysisResult(null, null, null);

            var spectra = ComputeSpectrogram(samples);
            if (spectra is null) return new AnalysisResult(null, null, null);

            var bpm = DetectBpm(spectra);
            var (key, camelot) = DetectKey(spectra);
            return new AnalysisResult(bpm, key, camelot);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new AnalysisResult(null, null, null); }
    }

    private static async Task<float[]?> DecodeAsync(string path, string ffmpeg, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(ffmpeg) ? "ffmpeg" : ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "quiet", "-i", path, "-ac", "1", "-ar",
                                  SampleRate.ToString(), "-t", MaxSeconds.ToString(),
                                  "-f", "f32le", "-" })
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi);
        if (proc is null) return null;

        using var buffer = new MemoryStream();
        await proc.StandardOutput.BaseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);

        var bytes = buffer.ToArray();
        if (bytes.Length < 4) return null;
        var samples = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
        return samples;
    }

    /// <summary>Magnitude spectrogram: [frame][bin].</summary>
    private static float[][]? ComputeSpectrogram(float[] y)
    {
        int frames = 1 + (y.Length - FftSize) / HopSize;
        if (frames < 8) return null;

        var window = new double[FftSize];
        for (int i = 0; i < FftSize; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));

        int bins = FftSize / 2 + 1;
        var result = new float[frames][];
        var re = new double[FftSize];
        var im = new double[FftSize];

        for (int f = 0; f < frames; f++)
        {
            int offset = f * HopSize;
            for (int i = 0; i < FftSize; i++)
            {
                re[i] = y[offset + i] * window[i];
                im[i] = 0;
            }
            Fft(re, im);

            var mags = new float[bins];
            for (int b = 0; b < bins; b++)
                mags[b] = (float)Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
            result[f] = mags;
        }
        return result;
    }

    /// <summary>In-place iterative radix-2 Cooley-Tukey FFT. Length must be a power of two.</summary>
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            double wRe = Math.Cos(angle), wIm = Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tRe = re[b] * curRe - im[b] * curIm;
                    double tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                    re[a] += tRe; im[a] += tIm;
                    double nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }

    private static double? DetectBpm(float[][] spectra, double lo = 60, double hi = 200)
    {
        int frames = spectra.Length;
        if (frames < 64) return null;

        // Spectral flux: positive change in magnitude, summed across bins.
        var envelope = new double[frames - 1];
        for (int f = 1; f < frames; f++)
        {
            double sum = 0;
            var cur = spectra[f];
            var prev = spectra[f - 1];
            for (int b = 0; b < cur.Length; b++)
            {
                double d = cur[b] - prev[b];
                if (d > 0) sum += d;
            }
            envelope[f - 1] = sum;
        }

        double mean = envelope.Average();
        for (int i = 0; i < envelope.Length; i++) envelope[i] -= mean;
        if (envelope.All(v => Math.Abs(v) < 1e-9)) return null;

        double fps = (double)SampleRate / HopSize;
        int lagMin = Math.Max(2, (int)Math.Round(60.0 * fps / hi));
        int lagMax = Math.Min(envelope.Length - 1, (int)Math.Round(60.0 * fps / lo));
        if (lagMax <= lagMin) return null;

        // Autocorrelation, weighted by a log-normal prior around 120 BPM so we
        // don't lock onto half or double time.
        double zero = Autocorrelate(envelope, 0);
        if (zero <= 0) return null;

        double bestScore = double.NegativeInfinity;
        int bestLag = lagMin;
        var ac = new double[lagMax + 2];
        for (int lag = lagMin; lag <= lagMax; lag++)
        {
            ac[lag] = Autocorrelate(envelope, lag) / zero;
            double tempo = 60.0 * fps / lag;
            double prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(tempo / 120.0) / 0.9, 2));
            double score = ac[lag] * prior;
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }

        // Parabolic interpolation for sub-lag precision.
        double refined = bestLag;
        if (bestLag > lagMin && bestLag < lagMax)
        {
            double a = ac[bestLag - 1], b = ac[bestLag], c = ac[bestLag + 1];
            double denom = a - 2 * b + c;
            if (Math.Abs(denom) > 1e-12) refined = bestLag + 0.5 * (a - c) / denom;
        }

        double bpm = 60.0 * fps / refined;
        while (bpm < lo) bpm *= 2;
        while (bpm > hi) bpm /= 2;
        return Math.Round(bpm, 1);
    }

    private static double Autocorrelate(double[] x, int lag)
    {
        double sum = 0;
        for (int i = 0; i + lag < x.Length; i++) sum += x[i] * x[i + lag];
        return sum;
    }

    private static (string? key, string? camelot) DetectKey(float[][] spectra)
    {
        int bins = spectra[0].Length;
        var pitchClass = new int[bins];
        var usable = new bool[bins];

        for (int b = 0; b < bins; b++)
        {
            double freq = (double)b * SampleRate / FftSize;
            usable[b] = freq > 55 && freq < 2200;
            if (!usable[b]) continue;
            double midi = 69 + 12 * Math.Log2(freq / 440.0);
            pitchClass[b] = ((int)Math.Round(midi) % 12 + 12) % 12;
        }

        var chroma = new double[12];
        foreach (var frame in spectra)
            for (int b = 0; b < bins; b++)
                if (usable[b]) chroma[pitchClass[b]] += frame[b];

        double total = chroma.Sum();
        if (total <= 0) return (null, null);
        for (int i = 0; i < 12; i++) chroma[i] /= total;

        double best = double.NegativeInfinity;
        int bestRoot = 0;
        bool bestIsMajor = true;

        for (int shift = 0; shift < 12; shift++)
        {
            double major = Correlate(chroma, MajorProfile, shift);
            double minor = Correlate(chroma, MinorProfile, shift);
            if (major > best) { best = major; bestRoot = shift; bestIsMajor = true; }
            if (minor > best) { best = minor; bestRoot = shift; bestIsMajor = false; }
        }

        var key = Pitches[bestRoot] + (bestIsMajor ? "" : "m");
        var camelot = (bestIsMajor ? CamelotMajor : CamelotMinor)[bestRoot];
        return (key, camelot);
    }

    private static double Correlate(double[] chroma, double[] profile, int shift)
    {
        double chromaMean = chroma.Average();
        double profileMean = profile.Average();
        double num = 0, dA = 0, dB = 0;
        for (int i = 0; i < 12; i++)
        {
            double a = chroma[i] - chromaMean;
            double b = profile[(i - shift + 12) % 12] - profileMean;
            num += a * b; dA += a * a; dB += b * b;
        }
        double denom = Math.Sqrt(dA * dB);
        return denom < 1e-12 ? -1 : num / denom;
    }
}
