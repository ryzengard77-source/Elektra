/*
 * Creator: Justin L. Ross | Farmington, Maine. | Remote Location: Iceland | Black Star Research Facility.
 */

using Elektra.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using RNNoise.NET;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using Vosk;

namespace Elektra.Audio;

public sealed class AudioCaptureService(
    IOptions<AssistantOptions> options,
    UserPreferencesService prefsService,
    ChannelWriter<string> utteranceWriter,
    AudioTelemetryBridge telemetry,
    ILogger<AudioCaptureService> logger) : IDisposable
{
    private const int CaptureSampleRate = 48000;
    private const int VoskSampleRate = 16000;
    private static readonly string[] ElectraVariant = ["electra"];

    private readonly AssistantOptions _options = options.Value;
    private string[] _wakePhrases = ["elektra"];
    private string[][] _wakePhraseWordSequences = [["elektra"]];
    private long _nextWakePartialCheckTicks;
    private readonly Lock _deviceLock = new();
    private readonly object _recognizerLock = new();
    private readonly AdaptiveEnergyVadDetector _energyVad = new();

    private WaveInEvent? _waveIn;
    private Denoiser? _denoiser;
    private Model? _voskModel;
    private VoskRecognizer? _openDomainRecognizer;
    private VoskRecognizer? _stopWordRecognizer;
    private VadHangoverGate? _vadGate;
    private BufferedWaveProvider? _denoised48kBuffer;
    private IWaveProvider? _vosk16kPcmProvider;

    private readonly List<float> _frameAccumulator = new(VadHangoverGate.FrameSize * 2);
    private readonly float[] _rnnoiseFrame = new float[VadHangoverGate.FrameSize];
    private readonly byte[] _denoisedByteBuffer = new byte[VadHangoverGate.FrameSize * sizeof(float)];
    private readonly byte[] _voskReadBuffer = new byte[4096];

    private int _isAwake;
    private long _awakeDeadlineTicks;
    private int _currentDeviceNumber;
    private bool _disposed;

    public event Action? OnWakeWordDetected;
    public event Action? OnStopWordDetected;
    public bool IsAwake => Volatile.Read(ref _isAwake) == 1;
    public int CurrentDeviceNumber => Volatile.Read(ref _currentDeviceNumber);

    public bool InitializeAndStart(string modelPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Directory.Exists(modelPath))
        {
            logger.LogError("Vosk speech model directory not found at '{Path}'.", modelPath);
            return false;
        }

        if (WaveInEvent.DeviceCount == 0)
            throw new InvalidOperationException("No microphone input device is available.");

        Vosk.Vosk.SetLogLevel(-1);
        _voskModel = new Model(modelPath);

        _wakePhrases = (_options.WakePhrases ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .DefaultIfEmpty("elektra")
            .ToArray();
        _wakePhraseWordSequences = _wakePhrases
            .Select(phrase => phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

        _openDomainRecognizer = new VoskRecognizer(_voskModel, VoskSampleRate);
        _openDomainRecognizer.SetMaxAlternatives(0);
        _openDomainRecognizer.SetWords(false);

        _stopWordRecognizer = new VoskRecognizer(
            _voskModel,
            VoskSampleRate,
            JsonSerializer.Serialize(new[] { "stop", "[unk]" }));
        _stopWordRecognizer.SetMaxAlternatives(0);
        _stopWordRecognizer.SetWords(false);

        try
        {
            _denoiser = new Denoiser();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            logger.LogWarning(ex, "RNNoise is unavailable; using energy-based voice activity detection without denoising.");
        }

        _vadGate = new VadHangoverGate(
            _options.VadProbabilityThreshold,
            _options.VadHangoverMs,
            _options.VadPreRollMs);

        _denoised48kBuffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(CaptureSampleRate, 1))
        {
            DiscardOnBufferOverflow = true,
            ReadFully = false
        };

        _vosk16kPcmProvider = new Pcm16DownsamplingWaveProvider(
            _denoised48kBuffer.ToSampleProvider(), VoskSampleRate);

        if (!SwitchDevice(ResolvePreferredDeviceIndex(), persistPreference: false))
            throw new InvalidOperationException("The configured microphone input device is unavailable.");
        telemetry.CurrentState = PlasmaState.Idle;
        return true;
    }

    private int ResolvePreferredDeviceIndex()
    {
        int count = WaveInEvent.DeviceCount;
        if (count == 0) return 0;

        string? saved = prefsService.Current.PreferredMicrophoneName;
        if (!string.IsNullOrWhiteSpace(saved))
        {
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(WaveInEvent.GetCapabilities(i).ProductName.Trim(), saved, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        return (_options.AudioDeviceNumber >= 0 && _options.AudioDeviceNumber < count)
            ? _options.AudioDeviceNumber
            : 0;
    }

    public bool SwitchDevice(int newDeviceNumber, bool persistPreference = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (newDeviceNumber < 0 || newDeviceNumber >= WaveInEvent.DeviceCount)
            return false;

        lock (_deviceLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnAudioDataAvailable;
                try
                {
                    _waveIn.StopRecording();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to stop the previous microphone before switching devices.");
                }
                _waveIn.Dispose();
                _waveIn = null;
            }

            _frameAccumulator.Clear();
            _vadGate?.Reset();
            _energyVad.Reset();
            _denoised48kBuffer?.ClearBuffer();
            telemetry.CurrentRms = 0f;

            var nextWaveIn = new WaveInEvent
            {
                DeviceNumber = newDeviceNumber,
                WaveFormat = new WaveFormat(CaptureSampleRate, 16, 1),
                BufferMilliseconds = 40
            };
            nextWaveIn.DataAvailable += OnAudioDataAvailable;
            try
            {
                nextWaveIn.StartRecording();
            }
            catch (Exception ex)
            {
                nextWaveIn.DataAvailable -= OnAudioDataAvailable;
                nextWaveIn.Dispose();
                logger.LogError(ex, "Failed to start microphone capture on device {DeviceNumber}.", newDeviceNumber);
                return false;
            }

            _waveIn = nextWaveIn;
            Interlocked.Exchange(ref _currentDeviceNumber, newDeviceNumber);

            string name = WaveInEvent.GetCapabilities(newDeviceNumber).ProductName.Trim();
            if (persistPreference)
            {
                prefsService.Update(p => p with { PreferredMicrophoneName = name }, debounceMs: 100);
            }

            logger.LogInformation("Active microphone set to [{Index}] {Name}", newDeviceNumber, name);
            return true;
        }
    }

    public void TriggerAwakeMode()
    {
        lock (_recognizerLock)
        {
            if (_disposed || _voskModel == null) return;

            long deadline = Stopwatch.GetTimestamp() +
                (long)(_options.ActiveListeningTimeoutSeconds * Stopwatch.Frequency);
            Interlocked.Exchange(ref _awakeDeadlineTicks, deadline);
            Interlocked.Exchange(ref _isAwake, 1);

            _openDomainRecognizer?.Reset();
            telemetry.CurrentState = PlasmaState.Listening;
            logger.LogInformation("Wake phrase activated; listening for up to {TimeoutSeconds} seconds.",
                _options.ActiveListeningTimeoutSeconds);
            OnWakeWordDetected?.Invoke();
        }
    }

    public void EnterWakeWordMode()
    {
        lock (_recognizerLock)
        {
            Interlocked.Exchange(ref _isAwake, 0);
            _openDomainRecognizer?.Reset();
            if (!telemetry.IsAssistantSpeaking)
            {
                telemetry.CurrentState = PlasmaState.Idle;
            }
        }
    }

    private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_recognizerLock)
        {
            if (_disposed) return;
            try
            {
                ProcessAudioData(e);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Audio processing failed for the active microphone.");
                telemetry.CurrentState = PlasmaState.Idle;
            }
        }
    }

    private void ProcessAudioData(WaveInEventArgs e)
    {
        if (_vadGate == null || _openDomainRecognizer == null || _vosk16kPcmProvider == null)
            return;

        bool isMuted = telemetry.IsAssistantSpeaking;

        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            _frameAccumulator.Add(BitConverter.ToInt16(e.Buffer, i));

        while (_frameAccumulator.Count >= VadHangoverGate.FrameSize)
        {
            _frameAccumulator.CopyTo(0, _rnnoiseFrame, 0, VadHangoverGate.FrameSize);
            _frameAccumulator.RemoveRange(0, VadHangoverGate.FrameSize);

            if (isMuted)
            {
                WriteFrameToResampler(_rnnoiseFrame);
                continue;
            }

            float vadScore = _denoiser?.Denoise(_rnnoiseFrame) ??
                _energyVad.CalculateScore(_rnnoiseFrame, _options.VadProbabilityThreshold);
            _vadGate.ProcessFrame(vadScore, isMuted, _rnnoiseFrame, WriteFrameToResampler);

            if (!isMuted)
            {
                float sumSq = 0f;
                for (int i = 0; i < VadHangoverGate.FrameSize; i++)
                {
                    float norm = _rnnoiseFrame[i] / 32768f;
                    sumSq += norm * norm;
                }
                telemetry.CurrentRms = Math.Clamp(MathF.Sqrt(sumSq / VadHangoverGate.FrameSize) * 4.5f, 0f, 1f);
            }
        }

        if (IsAwake && Stopwatch.GetTimestamp() > Volatile.Read(ref _awakeDeadlineTicks))
            EnterWakeWordMode();

        int bytesRead;
        while ((bytesRead = _vosk16kPcmProvider.Read(_voskReadBuffer, 0, _voskReadBuffer.Length)) > 0)
        {
            if (isMuted)
            {
                if (_stopWordRecognizer == null)
                    continue;

                bool stopFinal = _stopWordRecognizer.AcceptWaveform(_voskReadBuffer, bytesRead);
                string? stopText = stopFinal
                    ? ExtractText(_stopWordRecognizer.Result(), "text")
                    : null;
                if (string.Equals(stopText?.Trim().TrimEnd('.', '!', '?'), "stop", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation("Stop voice command detected during speech playback.");
                    OnStopWordDetected?.Invoke();
                    _stopWordRecognizer.Reset();
                }
                continue;
            }

            _stopWordRecognizer?.Reset();

            if (!IsAwake)
            {
                bool isFinal = _openDomainRecognizer.AcceptWaveform(_voskReadBuffer, bytesRead);
                string? heard = null;
                if (isFinal)
                {
                    _nextWakePartialCheckTicks = 0;
                    heard = ExtractText(_openDomainRecognizer.Result(), "text");
                }
                else
                {
                    long now = Stopwatch.GetTimestamp();
                    if (now >= _nextWakePartialCheckTicks)
                    {
                        _nextWakePartialCheckTicks = now + Stopwatch.Frequency / 5;
                        heard = ExtractText(_openDomainRecognizer.PartialResult(), "partial");
                    }
                }

                if (!string.IsNullOrWhiteSpace(heard) && IsWakePhrase(heard))
                {
                    logger.LogInformation("Wake phrase recognized as '{Transcript}'.", heard);
                    TriggerAwakeMode();
                }
            }
            else
            {
                if (_vadGate.IsGateOpen)
                {
                    long ext = Stopwatch.GetTimestamp() +
                        (long)(_options.ActiveListeningTimeoutSeconds * Stopwatch.Frequency);
                    Interlocked.Exchange(ref _awakeDeadlineTicks, ext);
                }

                if (_openDomainRecognizer.AcceptWaveform(_voskReadBuffer, bytesRead))
                {
                    string? question = ExtractText(_openDomainRecognizer.Result(), "text");
                    if (!string.IsNullOrWhiteSpace(question))
                    {
                        EnterWakeWordMode();
                        if (!utteranceWriter.TryWrite(question))
                            logger.LogWarning("Recognized utterance was dropped because the processing queue is full.");
                    }
                }
            }
        }
    }

    private bool IsWakePhrase(string recognizedText)
    {
        ReadOnlySpan<char> text = recognizedText.AsSpan();
        for (int i = 0; i < _wakePhraseWordSequences.Length; i++)
        {
            string[] phraseWords = _wakePhraseWordSequences[i];
            if (ContainsWordSequence(text, phraseWords) ||
                (_wakePhrases[i].Equals("elektra", StringComparison.Ordinal) &&
                 ContainsWordSequence(text, ElectraVariant)))
                return true;
        }
        return false;
    }

    private static bool ContainsWordSequence(ReadOnlySpan<char> text, string[] phraseWords)
    {
        int matched = 0;
        int index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && !char.IsLetterOrDigit(text[index]))
                index++;
            int start = index;
            while (index < text.Length && char.IsLetterOrDigit(text[index]))
                index++;
            if (start == index)
                continue;

            ReadOnlySpan<char> word = text[start..index];
            if (word.Equals(phraseWords[matched], StringComparison.OrdinalIgnoreCase))
            {
                matched++;
                if (matched == phraseWords.Length)
                    return true;
            }
            else
            {
                matched = word.Equals(phraseWords[0], StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
        }

        return false;
    }

    private void WriteFrameToResampler(ReadOnlySpan<float> frame)
    {
        if (frame.IsEmpty)
        {
            Array.Clear(_denoisedByteBuffer, 0, _denoisedByteBuffer.Length);
        }
        else
        {
            for (int i = 0; i < VadHangoverGate.FrameSize; i++)
            {
                BitConverter.TryWriteBytes(
                    _denoisedByteBuffer.AsSpan(i * 4, 4),
                    Math.Clamp(frame[i] / 32768f, -1f, 1f));
            }
        }
        _denoised48kBuffer!.AddSamples(_denoisedByteBuffer, 0, _denoisedByteBuffer.Length);
    }

    private static string? ExtractText(string json, string propName)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(propName, out var p) ? p.GetString()?.Trim() : null;
    }

    public void Dispose()
    {
        lock (_deviceLock)
        {
            lock (_recognizerLock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnAudioDataAvailable;
                try
                {
                    _waveIn.StopRecording();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to stop microphone capture during shutdown.");
                }
                _waveIn.Dispose();
                _waveIn = null;
            }
        }

        lock (_recognizerLock)
        {
            _openDomainRecognizer?.Dispose();
            _stopWordRecognizer?.Dispose();
            _voskModel?.Dispose();
            _denoiser?.Dispose();
        }
        utteranceWriter.TryComplete();
    }
}