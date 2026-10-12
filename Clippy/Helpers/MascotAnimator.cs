#nullable enable
using Clippy.Core.Classes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using global::Windows.Foundation;
using global::Windows.Foundation.Metadata;
using global::Windows.UI.ViewManagement;

namespace Clippy.Helpers
{
    // Animate the artwork, never the HWND or its layout/hit target.
    internal sealed class MascotAnimator : IDisposable
    {
        private readonly Image image;
        private readonly FrameworkElement button;
        private readonly Func<bool> enabled;
        private readonly DispatcherQueue dispatcher;
        private readonly UISettings system = new();
        private readonly CompositeTransform transform = new();
        private readonly bool hasSystemEvent;
        private Storyboard? current;
        private bool active = true, hovered, disposed;

        public MascotAnimator(Image image, FrameworkElement button, Func<bool> enabled)
        {
            this.image = image; this.button = button; this.enabled = enabled;
            dispatcher = image.DispatcherQueue;
            image.RenderTransformOrigin = new Point(0.5, 0.5);
            image.RenderTransform = transform;
            image.Loaded += Loaded;
            image.Unloaded += Unloaded;
            button.PointerEntered += Entered;
            button.PointerExited += Exited;
            hasSystemEvent = ApiInformation.IsEventPresent("Windows.UI.ViewManagement.UISettings", "AnimationsEnabledChanged");
            if (hasSystemEvent) system.AnimationsEnabledChanged += SystemChanged;
            Refresh();
        }

        private bool CanAnimate => !disposed && MascotMotion.ShouldRun(enabled(), system.AnimationsEnabled, active, image.IsLoaded);
        private void Loaded(object sender, RoutedEventArgs args) => Refresh();
        private void Unloaded(object sender, RoutedEventArgs args) => StopAndReset();
        private void SystemChanged(UISettings sender, object args) => dispatcher.TryEnqueue(Refresh);
        private void Entered(object sender, PointerRoutedEventArgs args) { hovered = true; Transition(true); }
        private void Exited(object sender, PointerRoutedEventArgs args) { hovered = false; Transition(false); }
        public void Suspend() { active = false; hovered = false; StopAndReset(); }
        public void Resume() { if (disposed || active) return; active = true; Refresh(); }

        public void Refresh()
        {
            if (disposed) return;
            StopAndReset();
            if (!CanAnimate) return;
            if (hovered) Transition(true); else StartIdle();
        }

        private void StartIdle()
        {
            var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
            AddFrames(storyboard, "Rotation", new[] { (0d, 0d), (1.25, MascotMotion.IdleRotation), (3.75, -MascotMotion.IdleRotation), (MascotMotion.CycleSeconds, 0d) });
            AddFrames(storyboard, "TranslateY", new[] { (0d, 0d), (2.5, -1.5), (MascotMotion.CycleSeconds, 0d) });
            current = storyboard;
            storyboard.Begin();
        }

        private void AddFrames(Storyboard storyboard, string property, (double Seconds, double Value)[] frames)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            foreach (var frame in frames)
                animation.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(frame.Seconds)), Value = frame.Value,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        private void Transition(bool hover)
        {
            if (!CanAnimate) { StopAndReset(); return; }
            var rotation = transform.Rotation;
            var y = transform.TranslateY;
            StopAndReset();
            transform.Rotation = rotation; transform.TranslateY = y;
            var storyboard = new Storyboard();
            AddTransition(storyboard, "Rotation", rotation, hover ? -MascotMotion.HoverRotation : 0);
            AddTransition(storyboard, "TranslateY", y, hover ? -MascotMotion.HoverLift : 0);
            if (!hover) storyboard.Completed += (_, _) =>
            {
                if (!ReferenceEquals(current, storyboard) || !CanAnimate || hovered) return;
                StopAndReset(); StartIdle();
            };
            current = storyboard;
            storyboard.Begin();
        }

        private void AddTransition(Storyboard storyboard, string property, double from, double to)
        {
            var animation = new DoubleAnimation
            {
                From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(animation, transform);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        private void StopAndReset()
        {
            current?.Stop(); current = null;
            transform.Rotation = 0; transform.TranslateY = 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; StopAndReset();
            image.Loaded -= Loaded; image.Unloaded -= Unloaded;
            button.PointerEntered -= Entered; button.PointerExited -= Exited;
            if (hasSystemEvent) system.AnimationsEnabledChanged -= SystemChanged;
        }
    }
}
