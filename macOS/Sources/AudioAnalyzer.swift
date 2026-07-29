import Accelerate
import Foundation

struct AnalysisResult {
    var bpm: Double?
    var key: String?
    var camelot: String?
}

/// BPM and musical key straight off the waveform. ffmpeg decodes to mono float32,
/// then spectral-flux autocorrelation gives tempo and a Krumhansl-Schmuckler
/// chroma correlation gives key. No external audio library.
///
/// Same algorithm as the Windows build, with the FFT and the inner loops handed
/// to Accelerate — a four-minute track lands in about three seconds rather than
/// eight, and most of that is still ffmpeg decoding.
enum AudioAnalyzer {
    private static let sampleRate = 22050
    private static let fftSize = 2048
    private static let log2n = vDSP_Length(11)      // 2^11 == 2048
    private static let hopSize = 512
    private static let maxSeconds = 420

    private static let pitches =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]

    // Camelot wheel, indexed by pitch class.
    private static let camelotMajor =
        ["8B", "3B", "10B", "5B", "12B", "7B", "2B", "9B", "4B", "11B", "6B", "1B"]
    private static let camelotMinor =
        ["5A", "12A", "7A", "2A", "9A", "4A", "11A", "6A", "1A", "8A", "3A", "10A"]

    // Krumhansl-Kessler key profiles.
    private static let majorProfile: [Double] =
        [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88]
    private static let minorProfile: [Double] =
        [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17]

    static func analyze(path: String, ffmpegPath: String) async -> AnalysisResult {
        do {
            guard let samples = try await decode(path: path, ffmpeg: ffmpegPath),
                  samples.count >= sampleRate
            else { return AnalysisResult() }

            guard let spectra = spectrogram(samples) else { return AnalysisResult() }

            let bpm = detectBpm(spectra)
            let (key, camelot) = detectKey(spectra)
            return AnalysisResult(bpm: bpm, key: key, camelot: camelot)
        } catch {
            return AnalysisResult()
        }
    }

    private static func decode(path: String, ffmpeg: String) async throws -> [Float]? {
        let arguments = [
            "-v", "quiet", "-i", path,
            "-ac", "1", "-ar", String(sampleRate),
            "-t", String(maxSeconds),
            "-f", "f32le", "-",
        ]
        let executable = ffmpeg.isBlank ? "ffmpeg" : ffmpeg
        let data = try await ProcessRunner.runCapturingData(executable, arguments)
        guard data.count >= 4 else { return nil }

        return data.withUnsafeBytes { raw -> [Float] in
            Array(raw.bindMemory(to: Float.self))
        }
    }

    /// Magnitude spectrogram: [frame][bin].
    private static func spectrogram(_ y: [Float]) -> [[Float]]? {
        let frames = 1 + (y.count - fftSize) / hopSize
        guard frames >= 8 else { return nil }

        guard let setup = vDSP_create_fftsetup(log2n, FFTRadix(kFFTRadix2)) else { return nil }
        defer { vDSP_destroy_fftsetup(setup) }

        // Hann window, matching the reference implementation exactly.
        var window = [Float](repeating: 0, count: fftSize)
        vDSP_hann_window(&window, vDSP_Length(fftSize), Int32(vDSP_HANN_DENORM))

        let bins = fftSize / 2
        var result = [[Float]](repeating: [], count: frames)

        var windowed = [Float](repeating: 0, count: fftSize)
        var real = [Float](repeating: 0, count: bins)
        var imaginary = [Float](repeating: 0, count: bins)

        for f in 0..<frames {
            let offset = f * hopSize
            y.withUnsafeBufferPointer { source in
                vDSP_vmul(source.baseAddress! + offset, 1, window, 1, &windowed, 1,
                          vDSP_Length(fftSize))
            }

            var magnitudes = [Float](repeating: 0, count: bins)

            real.withUnsafeMutableBufferPointer { realBuffer in
                imaginary.withUnsafeMutableBufferPointer { imaginaryBuffer in
                    var split = DSPSplitComplex(
                        realp: realBuffer.baseAddress!, imagp: imaginaryBuffer.baseAddress!)

                    windowed.withUnsafeBufferPointer { input in
                        input.baseAddress!.withMemoryRebound(
                            to: DSPComplex.self, capacity: bins
                        ) { complex in
                            vDSP_ctoz(complex, 2, &split, 1, vDSP_Length(bins))
                        }
                    }

                    vDSP_fft_zrip(setup, &split, 1, log2n, FFTDirection(FFT_FORWARD))

                    // vDSP packs the Nyquist bin into imagp[0]. Leaving it there
                    // would add a phantom low-frequency component to every frame.
                    split.imagp[0] = 0

                    vDSP_zvabs(&split, 1, &magnitudes, 1, vDSP_Length(bins))
                }
            }

            result[f] = magnitudes
        }
        return result
    }

    private static func detectBpm(_ spectra: [[Float]], lo: Double = 60, hi: Double = 200) -> Double? {
        let frames = spectra.count
        guard frames >= 64 else { return nil }

        // Spectral flux: positive change in magnitude, summed across bins.
        var envelope = [Float](repeating: 0, count: frames - 1)
        let bins = spectra[0].count
        var difference = [Float](repeating: 0, count: bins)
        var zero: Float = 0

        for f in 1..<frames {
            vDSP_vsub(spectra[f - 1], 1, spectra[f], 1, &difference, 1, vDSP_Length(bins))
            // Clip the negatives away, then sum: that is the positive flux.
            vDSP_vthres(difference, 1, &zero, &difference, 1, vDSP_Length(bins))
            var sum: Float = 0
            vDSP_sve(difference, 1, &sum, vDSP_Length(bins))
            envelope[f - 1] = sum
        }

        var mean: Float = 0
        vDSP_meanv(envelope, 1, &mean, vDSP_Length(envelope.count))
        var negativeMean = -mean
        vDSP_vsadd(envelope, 1, &negativeMean, &envelope, 1, vDSP_Length(envelope.count))

        var peak: Float = 0
        vDSP_maxmgv(envelope, 1, &peak, vDSP_Length(envelope.count))
        guard peak > 1e-9 else { return nil }

        let fps = Double(sampleRate) / Double(hopSize)
        let lagMin = max(2, Int((60.0 * fps / hi).rounded()))
        let lagMax = min(envelope.count - 1, Int((60.0 * fps / lo).rounded()))
        guard lagMax > lagMin else { return nil }

        // Autocorrelation, weighted by a log-normal prior around 120 BPM so we
        // don't lock onto half or double time.
        let zeroLag = autocorrelate(envelope, lag: 0)
        guard zeroLag > 0 else { return nil }

        var correlation = [Double](repeating: 0, count: lagMax + 2)
        var bestScore = -Double.infinity
        var bestLag = lagMin

        for lag in lagMin...lagMax {
            correlation[lag] = autocorrelate(envelope, lag: lag) / zeroLag
            let tempo = 60.0 * fps / Double(lag)
            let prior = exp(-0.5 * pow(log2(tempo / 120.0) / 0.9, 2))
            let score = correlation[lag] * prior
            if score > bestScore { bestScore = score; bestLag = lag }
        }

        // Parabolic interpolation for sub-lag precision.
        var refined = Double(bestLag)
        if bestLag > lagMin && bestLag < lagMax {
            let a = correlation[bestLag - 1]
            let b = correlation[bestLag]
            let c = correlation[bestLag + 1]
            let denominator = a - 2 * b + c
            if abs(denominator) > 1e-12 {
                refined = Double(bestLag) + 0.5 * (a - c) / denominator
            }
        }

        var bpm = 60.0 * fps / refined
        while bpm < lo { bpm *= 2 }
        while bpm > hi { bpm /= 2 }
        return (bpm * 10).rounded() / 10
    }

    private static func autocorrelate(_ x: [Float], lag: Int) -> Double {
        guard lag < x.count else { return 0 }
        let count = vDSP_Length(x.count - lag)
        var result: Float = 0
        x.withUnsafeBufferPointer { buffer in
            vDSP_dotpr(buffer.baseAddress!, 1, buffer.baseAddress! + lag, 1, &result, count)
        }
        return Double(result)
    }

    private static func detectKey(_ spectra: [[Float]]) -> (key: String?, camelot: String?) {
        guard let first = spectra.first else { return (nil, nil) }
        let bins = first.count

        var pitchClass = [Int](repeating: 0, count: bins)
        var usable = [Bool](repeating: false, count: bins)

        for b in 0..<bins {
            let frequency = Double(b) * Double(sampleRate) / Double(fftSize)
            usable[b] = frequency > 55 && frequency < 2200
            guard usable[b] else { continue }
            let midi = 69 + 12 * log2(frequency / 440.0)
            pitchClass[b] = ((Int(midi.rounded()) % 12) + 12) % 12
        }

        var chroma = [Double](repeating: 0, count: 12)
        for frame in spectra {
            for b in 0..<bins where usable[b] {
                chroma[pitchClass[b]] += Double(frame[b])
            }
        }

        let total = chroma.reduce(0, +)
        guard total > 0 else { return (nil, nil) }
        for i in 0..<12 { chroma[i] /= total }

        var best = -Double.infinity
        var bestRoot = 0
        var bestIsMajor = true

        for shift in 0..<12 {
            let major = correlate(chroma, majorProfile, shift: shift)
            let minor = correlate(chroma, minorProfile, shift: shift)
            if major > best { best = major; bestRoot = shift; bestIsMajor = true }
            if minor > best { best = minor; bestRoot = shift; bestIsMajor = false }
        }

        let key = pitches[bestRoot] + (bestIsMajor ? "" : "m")
        let camelot = (bestIsMajor ? camelotMajor : camelotMinor)[bestRoot]
        return (key, camelot)
    }

    private static func correlate(_ chroma: [Double], _ profile: [Double], shift: Int) -> Double {
        let chromaMean = chroma.reduce(0, +) / 12
        let profileMean = profile.reduce(0, +) / 12

        var numerator = 0.0, dA = 0.0, dB = 0.0
        for i in 0..<12 {
            let a = chroma[i] - chromaMean
            let b = profile[((i - shift) % 12 + 12) % 12] - profileMean
            numerator += a * b
            dA += a * a
            dB += b * b
        }
        let denominator = (dA * dB).squareRoot()
        return denominator < 1e-12 ? -1 : numerator / denominator
    }
}
