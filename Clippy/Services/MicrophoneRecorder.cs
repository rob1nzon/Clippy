#nullable enable
using Clippy.Core.Services;
using NAudio.Wave;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy.Services
{
    internal sealed class MicrophoneRecorder : IVoiceRecorder
    {
        private WaveInEvent? input;
        public void Stop() => input?.StopRecording();

        public async Task<byte[]> RecordAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (input != null) throw new InvalidOperationException("Microphone is already recording.");
            if (WaveInEvent.DeviceCount == 0) throw new InvalidOperationException("No microphone found.");
            using var pcm = new PcmRecordingBuffer();
            using var microphone = new WaveInEvent
            {
                DeviceNumber = -1, // Windows default input device (WAVE_MAPPER).
                WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100, NumberOfBuffers = 3
            };
            var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            microphone.DataAvailable += (_, args) =>
            {
                if (pcm.Append(args.Buffer, args.BytesRecorded)) microphone.StopRecording();
            };
            microphone.RecordingStopped += (_, args) =>
            {
                if (token.IsCancellationRequested) completion.TrySetCanceled(token);
                else if (args.Exception != null) completion.TrySetException(args.Exception);
                else completion.TrySetResult(pcm.ToWave());
            };
            input = microphone;
            void StopSafely()
            {
                try { microphone.StopRecording(); }
                catch (Exception error) { completion.TrySetException(error); }
            }
            try
            {
                microphone.StartRecording();
                // Native capture stops before any transcription begins, including at the limit.
                using var timer = new Timer(_ => StopSafely(), null, TimeSpan.FromSeconds(PcmRecordingBuffer.MaximumSeconds), Timeout.InfiniteTimeSpan);
                using var registration = token.Register(StopSafely);
                return await completion.Task;
            }
            finally { input = null; }
        }
    }
}
