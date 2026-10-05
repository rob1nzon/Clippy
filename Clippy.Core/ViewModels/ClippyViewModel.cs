using Clippy.Core.Classes;
using Clippy.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Clippy.Core.Interfaces;
using Clippy.Core.Enums;
using Clippy.Core.ViewModels.Messages;
using System.Collections.ObjectModel;
using Clippy.Core.Factories;
using System.Linq.Expressions;
using System.Diagnostics;

namespace Clippy.Core.ViewModels
{
    public partial class ClippyViewModel : ObservableObject
	{
		public ObservableCollection<MessageViewModel> MessagesVM = new();
		public ObservableCollection<IMessage> Messages = new();

		[ObservableProperty]
        private bool isClippyEnabled = false;

        [ObservableProperty]
        private bool isPinned = true;

		[ObservableProperty]
		private string currentText = "";

        [ObservableProperty]
        private byte[]? currentScreenshot;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendPromptCommand))]
        private bool isCapturingScreenshot;

        public int ConversationRevision { get; private set; }

		[ObservableProperty]
		private DateTime updatedAt = DateTime.Now;

		public IChatService ChatService;

        public ISettingsService SettingsService;

        public ClippyViewModel(IChatService chatService, ISettingsService settingsService)
        {
            ChatService = chatService;
            SettingsService = settingsService;
            isPinned = SettingsService.AutoPin;
			SetupChat();
        }

		private void SetupChat()
		{
			Messages.Add(new Message(Role.System, Constants.DEFAULT_SYSTEM_PROMPT));
		}

		private MessageViewModel AddMessage(IMessage message)
		{
			var ViewModel = MessageFactory.GetMessageViewModel(message);
			MessagesVM.Add(ViewModel);
			Messages.Add(message);
			return ViewModel;
		}

        private bool CanSendPrompt() => !IsCapturingScreenshot;

		[RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanSendPrompt))]
		public async Task SendPrompt(CancellationToken cancellationToken)
		{
			try
			{
				if (!IsCapturingScreenshot && (!string.IsNullOrWhiteSpace(CurrentText) || CurrentScreenshot != null))
				{
                    var userMessage = new Message(Role.User, string.IsNullOrWhiteSpace(CurrentText) ? "Что на этом скриншоте? Дай короткий полезный ответ." : CurrentText)
                    { ScreenshotJpeg = CurrentScreenshot };
                    var userVM = AddMessage(userMessage);
                    if (CurrentScreenshot != null) userVM.MessageText += "\n📷 Скриншот";
                    CurrentScreenshot = null;
					CurrentText = "";
					await Task.Delay(300);

                    var assistantMessage = new Message(Role.Assistant, "");
					var messageVM = AddMessage(assistantMessage) as ClippyMessageViewModel;
					UpdatedAt = DateTime.Now;
					messageVM?.StartStreamText(cancellationToken);
                    var responseText = new StringBuilder();

					try
					{
						await foreach (var chunk in ChatService.StreamChatAsync(Messages, cancellationToken))
                        {
                            responseText.Append(chunk);
							messageVM?.AddStreamText(chunk);
                        }
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						return; // Task cancelled so it is ok
					}
					catch (Exception e)
					{
						messageVM?.AddStreamText("\n\nConnection error: " + e.Message);
					}
                    finally
                    {
                        userMessage.ScreenshotJpeg = null;
                        messageVM?.EndStreamText();
                        var index = Messages.IndexOf(assistantMessage);
                        if (messageVM != null && index >= 0)
                            Messages[index] = new Message(Role.Assistant, responseText.ToString());
                    }
				}
			}
			catch (Exception e)
			{
				Debug.WriteLine(e.Message);
			}
		}

		[RelayCommand]
        private void RefreshChat()
		{
            ConversationRevision++;
            CurrentScreenshot = null;
			MessagesVM.Clear();
			Messages.Clear();
			SetupChat();
		}
    }
}
