using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Clippy.Core.Services
{
    public sealed class ScreenAdviceController : ObservableObject
    {
        private readonly ISettingsService settings;
        private readonly Func<byte[], CancellationToken, Task<string>> analyze;
        private readonly Func<DateTimeOffset> now;
        private CancellationTokenSource? active;
        private DateTimeOffset nextAttempt;
        private string? previousHash, previousAdvice;
        private bool faulted;
        private string tip = "", status = "Screen advice is off.";
        public string Tip { get => tip; private set => SetProperty(ref tip, value); }
        public string Status { get => status; private set => SetProperty(ref status, value); }
        public bool IsRunning => active != null;

        public ScreenAdviceController(ISettingsService settings, Func<byte[], CancellationToken, Task<string>> analyze,
            Func<DateTimeOffset>? now = null)
        {
            this.settings = settings; this.analyze = analyze; this.now = now ?? (() => DateTimeOffset.UtcNow);
            Reset();
        }

        public void Dismiss() => Tip = "";
        public void Reset()
        {
            active?.Cancel();
            Tip = "";
            previousHash = previousAdvice = null;
            faulted = false;
            // Enabling or restarting never captures immediately.
            nextAttempt = now().AddMinutes(Math.Max(5, Math.Min(60, settings.ScreenAdviceIntervalMinutes)));
            Status = settings.ScreenAdviceEnabled ? "Enabled. Waiting for the next interval; hidden Clippy and active chat pause capture." : "Screen advice is off.";
        }

        public async Task RunAsync(Func<CancellationToken, Task<byte[]?>> capture, bool canObserve, bool manual = false)
        {
            if (!settings.ScreenAdviceEnabled || IsRunning) return;
            if (!canObserve) { Status = "Paused: show Clippy and finish the current chat or input."; return; }
            if (!manual && (faulted || now() < nextAttempt)) return;
            nextAttempt = now().AddMinutes(Math.Max(5, Math.Min(60, settings.ScreenAdviceIntervalMinutes)));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            active = cancellation;
            OnPropertyChanged(nameof(IsRunning));
            try
            {
                Status = "Capturing one monitor and asking the configured model...";
                var jpeg = await capture(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!settings.ScreenAdviceEnabled) return;
                if (jpeg == null) { Status = "Paused: desktop is locked, unavailable, or a full-screen app is active."; return; }
                using var sha = SHA256.Create();
                var hash = Convert.ToBase64String(sha.ComputeHash(jpeg));
                if (!manual && hash == previousHash) { Status = "Screen unchanged; no request sent."; return; }
                var result = await analyze(jpeg, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!settings.ScreenAdviceEnabled) return;
                previousHash = hash;
                faulted = false;
                var clean = result.Trim();
                if (clean.Length == 0 || clean.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
                { Status = "No useful advice this time."; return; }
                // Keep the bubble short even if the model ignores its instructions.
                if (clean.Length > 240) clean = clean.Substring(0, 239).TrimEnd() + "…";
                if (clean == previousAdvice) { Status = "Repeated advice suppressed."; return; }
                previousAdvice = clean;
                Tip = clean;
                Status = "Advice received. Screenshots are not saved or added to chat history.";
            }
            catch (OperationCanceledException)
            {
                if (settings.ScreenAdviceEnabled && cancellation.IsCancellationRequested)
                    Status = "Request cancelled or timed out. No screenshot was saved.";
            }
            catch (Exception) when (cancellation.IsCancellationRequested) { }
            catch (Exception)
            {
                faulted = true;
                // Do not display server response bodies, which may echo image data.
                Status = "Screen advice failed. Check the connection and vision model; automatic requests are paused. Use Advice now to retry.";
            }
            finally { active = null; OnPropertyChanged(nameof(IsRunning)); }
        }
    }
}
