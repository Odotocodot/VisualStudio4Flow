dotnet publish Flow.Launcher.Plugin.VisualStudio -c Release -r win-x64 --no-self-contained
Compress-Archive -LiteralPath Flow.Launcher.Plugin.VisualStudio/bin/Release/win-x64/publish -DestinationPath Flow.Launcher.Plugin.VisualStudio/bin/VisualStudio.zip -Force