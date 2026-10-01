using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading;

namespace Clippy.Core.ViewModels.Messages
{
    public partial class ClippyMessageViewModel
    {
        [ObservableProperty]
        private string streamingText = "";

        [ObservableProperty]
        private bool isStreaming = false;

        public void StartStreamText(CancellationToken cancellationToken, int batchSize = 3, int delayMs = 15)
        {
            StreamingText = "";
            IsStreaming = true;
        }

        public void AddStreamText(string text) => StreamingText += text;

        public void EndStreamText()
        {
            MessageText = StreamingText;
            IsStreaming = false;
        }
    }
}
