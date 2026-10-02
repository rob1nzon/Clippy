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
        private int clippySize = Math.Clamp((int)Settings.Get("ClippySize", 100), 60, 200);
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
            set { Settings.Values["ServerUrl"] = value; Settings.Save(); SetProperty(ref serverUrl, value); }
        }

        private string model = (string)Settings.Get("Model", "local-model");
        public string Model
        {
            get => model;
            set { Settings.Values["Model"] = value; Settings.Save(); SetProperty(ref model, value); }
        }
    }
}
