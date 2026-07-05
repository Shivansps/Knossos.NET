# Knossos.NET<br />
![KnossosNET](https://i.imgur.com/xwWvwcn.png)
<br />
<br />
Knossos.NET, also known as KNet, is a multi-platform launcher for Freespace 2 Open using .NET 6.0 and AvaloniaUI<br />
<br />

<br /><br />
## **Current Status:**<br />
Version 1.3.x has been released!<br />
Check out our download page: https://knossoslauncher.com/<br />

<br /><br />
## **Main Improvements:**<br />
Knossos.NET offers greatly improved performance and stability over the old Python-based Knossos.  Because it is written in C#, builds can be released much more easily.  Knossos.NET also minimizes the amount of memory and VRAM used, so that slower computers will not suffer performance penalties when running FSO.  Knossos.NET furthermore supports compressed VPs, it can create compressed VPs when using the dev tab, and it can detect duplicate installations and hardlink them, reducing hard drive footprint. Finally Knossos.NET has additional features compared to original Knossos, while still maintaining feature parity.  Some of these features are detailed below.

<br /><br />
## **Mod Hardlinking:**<br />
Knossos.Net is able to create hardlinks between duplicate files or packages within the same mod.  If a file has not changed in a new version of an FSO mod, then a hardlink can be created.  Hardlinks are a way to tell the OS that we want the same file referenced in two different folders.  A hardlinked file will appear in both folders that reference it, and it will be counted again with every reference when calculating hard drive space, even though it only exists once on the disk.  Hardlinking can be disabled in Knossos.Net's settings tab, or during mod installation.
<br /><br />
This greately minimizes hard drive usage, but comes with two things to keep in mind: 
<br />
1) A hardlinked file will only be deleted once all of its references have been deleted.
2) If one copy of a hardlinked file is altered, then all the references point to the altered file. It really is multiple references to the same file.  If you want to edit a mod's hardlinked file, please create a dev version of it in the dev tab.  This will create a non-hardlinked version that can be safely edited.

<br /><br />
## **Quick Launch Cmdline:**<br />
KnossosNET supports direct mod launch by adding a Command Line argument, this will open the launcher, and will launch FSO to play a mod with all the current settings and configurations, and close the launcher.<br />
Example:<br />
<br />
KnossosNET.exe -playmod {mod-id} -version {mod-version} (optional) -exec {fso-exec-type} (optional) -build {buildid} (optional) -build-version {version} -cmdline "{additional cmd arguments for fso}"<br />
<br />
If no -build is given it will default to "FSO" if -build-version is also used. If -build-version is not used either mod default will be used.<br />
-build-version supports both a specific version and the special strings "NewerStable", "NewerNightly" and "NewerRc" to select the newest one of each condition that is installed.<br />
If no version is given (KnossosNET.exe -playmod mod-id) the highest version of the mod will be launched.<br />
If no fso-exec-type is given or "Default", or an invalid type is passed "Release" will be used instead. <br />
Exec type options:<br />
-Default<br />
-Release<br />
-Debug<br />
-Fred2<br />
-Fred2Debug<br />
-QtFred<br />
-QtFredDebug<br />
The "Settings" window on a mod displays the Command Line to play that mod directly via Quick Launch. You can copy that Quick Launch Command Line and use it to create a shortcut to easily launch that mod. <br />

<br /><br />
## **Tool Quick Launch:**<br />
You can launch installed mod development tools with a Command Line argument (just like how you can launch mods with QuickLaunch).<br />
KnossosNET.exe -tool {tool-id}

<br /><br />
## **Tray Icon Mode:**<br />
On 1.3.0 a feature to "close to tray" has been implemented, this can be enabled on global settings.<br />
Additionally, a Command Line argument (-traymode) has been added to Knossos.NET. By starting in this mode the launcher will start to tray and will not load UI resources until the user opens the UI for the first time.<br />
The tray icon allow to launch mods directly from there.<br />
![TrayIcon](https://i.imgur.com/cWF4WdT.png)

<br /><br />
## **Rendering Mode:**<br />
From 1.3.0 KnossosNET will render the UI in software mode by default to avoid use of the user GPU, but it can be set to run on hardware GPU rendering.
<br />
You can force the hardware rendering mode by using the "-hardware" Command Line argument or by setting an environment variable "KNET_RENDER_MODE" to "hardware".<br />
For versions older than 1.3.0 hardware rendering is used by default and can be set to run on software mode by using "-software" or KNET_RENDER_MODE" to "software".

<br /><br />
## **Mod Filters and tags:**<br />
![tags](https://i.imgur.com/KKVyuxc.png)
<br />
A filter and tags feature offers QOL improvements to mod search and listening.<br />
Filters are a physical button that will display all mods that contain that filter.<br />
Tags are words that will be used in the search function to improve search results. Before this only the mod name was used for the search.<br />
To add filters and tags to your mods please consult in the Discord channel or create a PR or issue at the GitHub link here: https://github.com/KnossosNET/KNet-General-Resources-Repo


<br /><br />
## **mod.ini Support:**<br />
Using legacy mod.ini files for mod folders is also supported, and some additional keys were added to extend support. mod.ini can be used to attempt to load an old mod or to manually add a mod to the launcher whiout having to write a mod.json file. The folder still has to be placed in the correct path inside the library as with any other mod.<br />

Details on how KnossosNET handles mod.ini and the new keys can be found here:<br />
https://wiki.hard-light.net/index.php/Mod.ini#KnossosNET_Support<br />

<br /><br />
## **CmdLine Priority Explained:**<br />
On KnossosNET there are multiples sources of Command Line arguments that are eventually joined into a single one to launch the game, repeating arguments are not allowed.<br />

- By default, the priority works in this order:<br />
  - _SystemCMD(Global Settings) > Global CMD > User/Mod CMD_

- Users also have the option to not apply the global Command Line on a per-mod basis by checking the box in a mod's settings. If checked, the priority is then only: <br />
  - _User/Mod CMD_


<br /><br />
## **Dev Environment:**<br />
- MSVC 2022<br />
- .NET 6.0.406(or newer) SDK https://dotnet.microsoft.com/en-us/download/dotnet/6.0<br />
- Avalonia Extension for Visual Studio https://marketplace.visualstudio.com/items?itemName=AvaloniaTeam.AvaloniaVS<br />
- (optional) .NET 10 SDK + Android workload for android building

<br /><br />
## **Included Libs:**<br />
- VP.NET<br />
- IonKiwi.lz4 (modified for LZ41 support)<br />

<br /><br />
## **Current NuGet Packages:**<br />
- Avalonia 11.2.3<br />
- Avalonia.Desktop 11.2.3<br />
- Avalonia.Control.DataGrid 11.2.3<br />
- Avalonia.Diagnostics 11.2.3<br />
- Avalonia.Markup.Xaml.Loader 11.2.3<br />
- Avalonia.Themes.Fluent 11.2.3<br />
- Avalonia.HtmlRenderer 11.0.0<br />
- CommunityToolkit.Mvvm 8.4.0<br />
- Microsoft.Extensions.Http 8.0.1<br />
- ini-parser-netstandard 2.5.2<br />
- SharpCompress 0.39.0<br />
- AnimatedImage.Avalonia 1.0.7<br />
- ObservableCollections 3.3.3<br />
- WindowsShortcutFactory 1.2.0<br />

<br /><br />
## **Compiling/Developing for Desktop (Windows/Linux/mac) on VS:**<br />
- Set Knossos.NET.Desktop as your starting project<br />
- Right click Knossos.NET.Android -> Unload Project (or install net 10 SDK + android workload)<br />

<br /><br />
## **Compiling for Linux and Mac:**<br />
- Right click on the Knossos.NET.Desktop project -> Publish<br />
- Export to folder<br />
- From there you can pick the destination environment on the list<br />
<br /><br />

## **Compiling for Android:**<br />
- Download and install the .NET 10 SDK<br />
- Open a console and run this command "dotnet workload install android" to install the Android target framework.<br />
- On a console navigate to the solution root folder of the Knossos Project<br />
- Run "dotnet build Knossos.NET.Android -c Release -p:BuildAndroid=true" this will create a signed apk<br />
- If you dont want to use apk for developing, you can run directly at your phone with this command<br />
  "dotnet run --project ./Knossos.NET.Android -c Debug -p:BuildAndroid=true"<br />
You will need the adb deamon already running for this or it would not be able to find the device. I generally use a tool called "scrcpy" so be able to use my phone from my pc during developing, starting this toll will already start the adb deamon for you and you can compile and run directly to your phone with this command.
<br /><br />

# Custom TC Launcher Mode<br />
![CustomLauncher](https://i.imgur.com/uCYnTvD.png)
<br />
<br />
Starting from 1.3.0, Knossos.NET supports "Custom TC Mode" which can be used to provide a customized launcher for a specific Total Conversion mod. "Custom TC Mode" supports customization via an additional json file or by hard-coding the data in.<br />
More information and examples here: https://github.com/KnossosNET/Knet-CustomTc-Mode-Example<br />