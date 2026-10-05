<div align="center">
<img src="https://store-images.s-microsoft.com/image/apps.44246.14443762301762232.2ced24ff-d71a-4ad1-a73b-15e6c23bbd32.a6fa0dac-fd6f-4580-b258-6c9285a502e5?w=120" alt="Picture" style="display: block; margin: 0 auto; height: 180px;width:185px"/>
</div>

<div align="center">
<h1>Clippy by FireCube</h1>

<a href="https://github.com/FireCubeStudios/Clippy"><img src="https://img.shields.io/badge/Contributions-welcome-green"></a> 

<a href="https://github.com/FireCubeStudios/Clippy/issues"><img src="https://img.shields.io/github/issues/FireCubeStudios/Clippy"></a>
<a href="https://github.com/FireCubeStudios/Clippy/fork"><img src="https://img.shields.io/github/forks/FireCubeStudios/Clippy"></a>
<a href="https://github.com/FireCubeStudios/Clippy/stargazers/"><img src="https://img.shields.io/github/stars/FireCubeStudios/Clippy"></a>

<p style="font-size:15px;">Clippy by FireCube (Not by Microsoft) brings back the infamous Clippit into your desktop powered by the OpenAI GPT 3.5 model (OpenAI key required as of this version).

Clippy can be pinned to the screen for quick access to chat or just be left for nostalgia.</p>
</div>


# Preview ✨

<p align="center">
  <img align="center" src="https://store-images.s-microsoft.com/image/apps.58226.14443762301762232.2ced24ff-d71a-4ad1-a73b-15e6c23bbd32.9f9e3b96-c525-4243-b113-b74a61416117?h=2160">
  </p>


<a href="https://apps.microsoft.com/store/detail/clippy-by-firecube/9NWK37S35V5T"><img width="35%" src="https://raw.githubusercontent.com/FireCubeStudios/Protecc/711c253df88f36aa63f55b88bafae16a979c69e0/Assets/Get_it_from_Microsoft_Badge.svg" alt="Get Clippy from Microsoft Store"></a>
  
Get Clippy on [Microsoft Store](https://apps.microsoft.com/store/detail/clippy-by-firecube/9NWK37S35V5T)

## Discord
- Join our [Discord Channel](https://discord.gg/3WYcKat)
  
  ### Architecture of app:
- CubeKit.UI - Contains GlowUI and helper methods
- Clippy.Core - Contains most of chat logic including interfaces IChatService and an implementation for OpenAI key official
- Clippy - App with UI, Windows, Message design and logic of UI (message loading)

## Local network model (llama.cpp)

Start an instruction/chat GGUF model on the server machine:

```sh
llama-server -m /path/to/model.gguf --host 0.0.0.0 --port 8080 --alias local-model --jinja
```

Allow inbound TCP port 8080 on the server's firewall for your local network.
In Clippy Settings, enter `http://192.168.1.10:8080/v1` (replace the IP),
model `local-model`, and the maximum response tokens. Click **Save connection**.
The `/v1` suffix is optional. API key may be empty; if the server uses
`--api-key`, enter the same key in Clippy. Keys are stored in Windows Credential
Locker; other settings are in `%LOCALAPPDATA%\Clippy\settings.json`.
New messages use the saved connection immediately. Refresh the chat after changing
models if you want to start a fresh conversation.

Enter sends a message; Shift+Enter inserts a new line. Empty transparent areas
around Clippy pass clicks through to the applications underneath.

Since v0.5.0, activating Clippy opens chat and focuses its input. New/reset chats
have no greeting. The lightbulb beside Reset runs **Advice now**. The screenshot
icon attaches an image of Clippy's monitor to the next message, with a preview
and a remove button. Nothing is sent until you press Send/Enter; you can add your
own question or send just the image with a default prompt. This manual attachment
does not enable automatic screen advice. It requires a vision model and may contain
private information. Images are not saved to disk and are released after the request,
so subsequent messages do not resend them. Resetting chat or changing the server URL
removes an unsent attachment.

See the [llama.cpp server documentation](https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md).

## MCP tools (v0.3.0)

Clippy can connect to one MCP server over HTTP, including a server on another
computer in your LAN. In Settings → MCP tools, enable MCP, enter the exact MCP
endpoint (for example `http://192.168.1.10:3001/mcp`), optionally enter its Bearer
token, and click **Save and test MCP**. The discovered tool names appear below.
The MCP token is stored separately from the model API key in Windows Credential
Locker. Disable MCP to return to ordinary chat.

The model must support function calling; start llama-server with `--jinja`.
Clippy sends the discovered tool schemas to the model, shows a confirmation
dialog with each requested tool's name and JSON arguments, then returns the
approved tool's result to the model. Declined tools are not executed. A request
is limited to seven tool-call rounds. Local stdio processes, MCP resources,
prompts and OAuth login are not part of this version.

The client uses the [official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk),
with the stable `2025-11-25` protocol. See also
[llama.cpp function calling](https://github.com/ggml-org/llama.cpp/blob/master/docs/function-calling.md).

## Optional screen advice (v0.4.0)

In Settings → **Optional screen advice**, turn the switch on, choose an interval
(5–60 minutes, default 15), click **Save screen advice** and confirm the screenshot
warning. **Advice now** tests it immediately while Clippy is visible, the input is
empty and no response is running. Since v0.4.1 this button is also beside the chat
reset button, and manual advice works with chat open. Automatic captures still pause
while chat is open. Switching off stops capture immediately. Changing the model server
URL also disables the feature; enable it again to consent to the new destination.

Clippy captures only the monitor containing the mascot, scales the image to at
most 1280 pixels on its longest side, and sends a JPEG to the configured model's
`/v1/chat/completions` endpoint. Images are held in memory, not saved to disk or
added to chat history. This request does not use MCP tools. A short Russian tip
appears beside the mascot for 25 seconds without opening chat or taking focus;
use × to dismiss it. Repeated tips are suppressed. Hidden Clippy, active chat or
input, an unavailable/locked desktop and full-screen apps pause captures. After
a request failure automatic requests pause until a manual retry or settings change.

**Privacy:** the entire selected monitor can include messages, passwords and other
private data. There is no automatic redaction before sending. The configured server
may retain screenshots; use only a server you trust. Turning this off cannot retract
images already sent to the server.

The model must support images. With local llama.cpp GGUF files, load a vision model
and its matching multimodal projector, for example:

```sh
llama-server -m vision-model.gguf --mmproj matching-mmproj.gguf --host 0.0.0.0 --port 8080 --alias local-model
```

See the [llama.cpp multimodal documentation](https://github.com/ggml-org/llama.cpp/blob/master/docs/multimodal.md).

## Build / download Windows EXE

In your fork, open **Actions → Build Clippy EXE → Run workflow** and select your
branch. After a successful run, download the **Clippy-win-x64** artifact, extract
the entire ZIP, and launch `Clippy.exe`. Keep the DLLs and Assets beside the EXE.
The artifact includes .NET and Windows App SDK; MSIX installation is not required.
Windows 10 version 1903 or newer is required (Windows 11 recommended).

To build locally, use Windows with Visual Studio 2022, the WinUI/.NET desktop
build tools, .NET 9 SDK, and Windows SDK 10.0.22621.0 or newer. In a Developer
PowerShell from the repository directory:

```powershell
msbuild Clippy\Clippy.csproj /restore /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:AppxPackageSigningEnabled=false /p:GenerateAppxPackageOnBuild=false /p:PublishDir="$PWD\artifacts\Clippy-win-x64\"
```

This WinUI app must be built on Windows. Click the tray icon to show Clippy;
right-click it for Show, Hide, Settings, and Exit. The tray can be disabled in
Settings. The Clippy size slider changes the mascot from 60% to 200% and saves
the choice. The window sits at the bottom-right of the current monitor's work
area and adapts to DPI scaling, taskbar position and available screen height.
To run on login, put a shortcut to `Clippy.exe` in the `shell:startup` folder.
See [Microsoft's self-contained deployment documentation](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps).

<hr>
<h6 align="center">© FireCubeStudios. 2023
<br>
All Rights Reserved</h6>
<p align="center">
	<a href="https://github.com/FireCubeStudios/Clippy/blob/master/LICENSE.txt"><img src="https://img.shields.io/static/v1.svg?style=for-the-badge&label=License&message=MIT&logoColor=d9e0ee&colorA=363a4f&colorB=b7bdf8"/></a>
</p>
