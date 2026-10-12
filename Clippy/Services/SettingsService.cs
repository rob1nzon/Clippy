using Clippy.Core.Services;
using Clippy.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clippy.Services
{
    public class SettingsService : ObservableObject, ISettingsService
    {
        private static readonly SettingsStore Settings = new();

        private bool autoPin = (bool)Settings.Get("AutoPin", true);
        public bool AutoPin
        {
            get => autoPin;
            set
            {
                Settings.Values["AutoPin"] = value;
                Settings.Save();
                SetProperty(ref autoPin, value);
            }
        }

        private bool trayClippy = (bool)Settings.Get("TrayClippy", true);
        private bool animateClippy = (bool)Settings.Get("AnimateClippy", true);
        public bool AnimateClippy
        {
            get => animateClippy;
            set { Settings.Values["AnimateClippy"] = value; Settings.Save(); SetProperty(ref animateClippy, value); }
        }

        public bool TrayClippy
        {
            get => trayClippy;
            set
            {
                Settings.Values["TrayClippy"] = value;
                Settings.Save();
                SetProperty(ref trayClippy, value);
            }
        }

        private bool translucentBackground = (bool)Settings.Get("TranslucentBackground", true);
        public bool TranslucentBackground
        {
            get => translucentBackground;
            set
            {
                Settings.Values["TranslucentBackground"] = value;
                Settings.Save();
                SetProperty(ref translucentBackground, value);
            }
        }

        private int tokens = (int)Settings.Get("Tokens", 512);
        public int Tokens
        {
            get => tokens;
            set
            {
                value = Math.Clamp(value, 1, 32768);
                Settings.Values["Tokens"] = value;
                Settings.Save();
                SetProperty(ref tokens, value);
            }
        }

        private bool keyboardEnabled = (bool)Settings.Get("KeyboardEnabled", true);
        public bool KeyboardEnabled
        {
            get => keyboardEnabled;
            set
            {
                Settings.Values["KeyboardEnabled"] = value;
                Settings.Save();
                SetProperty(ref keyboardEnabled, value);
            }
        }
        private bool mcpEnabled = (bool)Settings.Get("McpEnabled", false);
        private bool voiceInputEnabled = (bool)Settings.Get("VoiceInputEnabled", false);
        public bool VoiceInputEnabled
        {
            get => voiceInputEnabled;
            set { Settings.Values["VoiceInputEnabled"] = value; Settings.Save(); SetProperty(ref voiceInputEnabled, value); }
        }

        private string speechServerUrl = (string)Settings.Get("SpeechServerUrl", "http://localhost:8000/v1/audio/transcriptions");
        public string SpeechServerUrl
        {
            get => speechServerUrl;
            set
            {
                if (value != speechServerUrl && VoiceInputEnabled) VoiceInputEnabled = false;
                Settings.Values["SpeechServerUrl"] = value; Settings.Save(); SetProperty(ref speechServerUrl, value);
            }
        }

        private string speechModel = (string)Settings.Get("SpeechModel", "gigaam");
        public string SpeechModel
        {
            get => speechModel;
            set { Settings.Values["SpeechModel"] = value; Settings.Save(); SetProperty(ref speechModel, value); }
        }

        public bool McpEnabled
        {
            get => mcpEnabled;
            set { Settings.Values["McpEnabled"] = value; Settings.Save(); SetProperty(ref mcpEnabled, value); }
        }

        private string mcpServerUrl = (string)Settings.Get("McpServerUrl", "http://localhost:3001/mcp");
        public string McpServerUrl
        {
            get => mcpServerUrl;
            set { Settings.Values["McpServerUrl"] = value; Settings.Save(); SetProperty(ref mcpServerUrl, value); }
        }

        private int clippySize = Math.Clamp((int)Settings.Get("ClippySize", 100), 60, 200);
        private bool screenAdviceEnabled = (bool)Settings.Get("ScreenAdviceEnabled", false);
        public bool ScreenAdviceEnabled
        {
            get => screenAdviceEnabled;
            set { Settings.Values["ScreenAdviceEnabled"] = value; Settings.Save(); SetProperty(ref screenAdviceEnabled, value); }
        }

        private int screenAdviceIntervalMinutes = Math.Clamp((int)Settings.Get("ScreenAdviceIntervalMinutes", 15), 5, 60);
        public int ScreenAdviceIntervalMinutes
        {
            get => screenAdviceIntervalMinutes;
            set
            {
                value = Math.Clamp(value, 5, 60);
                Settings.Values["ScreenAdviceIntervalMinutes"] = value;
                Settings.Save();
                SetProperty(ref screenAdviceIntervalMinutes, value);
            }
        }

        public int ClippySize
        {
            get => clippySize;
            set
            {
                value = Math.Clamp(value, 60, 200);
                Settings.Values["ClippySize"] = value;
                Settings.Save();
                SetProperty(ref clippySize, value);
            }
        }

        private string serverUrl = (string)Settings.Get("ServerUrl", "http://localhost:8080/v1");
        public string ServerUrl
        {
            get => serverUrl;
            set
            {
                // Screenshot consent applies only to the endpoint displayed at opt-in.
                if (value != serverUrl && ScreenAdviceEnabled) ScreenAdviceEnabled = false;
                Settings.Values["ServerUrl"] = value; Settings.Save(); SetProperty(ref serverUrl, value);
            }
        }

        private string model = (string)Settings.Get("Model", "local-model");
        private string systemPrompt = (string)Settings.Get("SystemPrompt", Clippy.Core.Constants.DEFAULT_SYSTEM_PROMPT);
        public string SystemPrompt
        {
            get => systemPrompt;
            set
            {
                value ??= "";
                Settings.Values["SystemPrompt"] = value;
                Settings.Save();
                SetProperty(ref systemPrompt, value);
            }
        }

        public string Model
        {
            get => model;
            set { Settings.Values["Model"] = value; Settings.Save(); SetProperty(ref model, value); }
        }
    }
}
