#dotnet publish Flow.Launcher.Plugin.VisualStudio -c Release -r win-x64 --no-self-contained
#Compress-Archive -LiteralPath Flow.Launcher.Plugin.VisualStudio/bin/Release/win-x64/publish -DestinationPath Flow.Launcher.Plugin.VisualStudio/bin/VisualStudio.zip -Force


$PluginJson = Get-Content .\Flow.Launcher.Plugin.VisualStudio\plugin.json -Raw | ConvertFrom-Json

$Name = $PluginJson.Name 
$Version = $PluginJson.Version
$ActionKeyword = $PluginJson.ActionKeyword

if (!$Name) {
    Write-Output "Invalid Name"
    Exit
}

$confirmation = Read-Host "Is Name Valid: $Name ? [y/n]"
while($confirmation -ne "y")
{
    if ($confirmation -eq 'n') {exit}
    $confirmation = Read-Host "Ready? [y/n]"
}

$FullName = $Name + "-" + $Version

dotnet publish -c Release -r win-x64 --no-self-contained --property:PublishDir=.\bin\Release\$FullName
#Compress-Archive -LiteralPath .\bin\Release\$FullName -DestinationPath .\bin\"$FullName.zip" -Force

Do {
    $Flow = Get-Process | Where-Object -Property ProcessName -eq 'Flow.Launcher'
    if ($Flow) {
        Stop-Process $Flow
        Start-Sleep 1
    }
} Until (!$Flow)

$Folders = Get-ChildItem -Path $env:APPDATA\FlowLauncher\Plugins\ | Where-Object { $_ -Match "$Name-\d.\d.\d" }
foreach ($Folder in $Folders) {
    Remove-Item -Recurse $env:APPDATA\FlowLauncher\Plugins\$Folder\ -Force -ErrorAction Stop
}

Copy-Item -Recurse -LiteralPath ./Flow.Launcher.Plugin.VisualStudio/bin/Release/$FullName $env:APPDATA\FlowLauncher\Plugins\ -Force
$Flow = Start-Process $env:LOCALAPPDATA\FlowLauncher\Flow.Launcher.exe -PassThru

#Do {} While ($Flow.WaitForInputIdle(5000) -ne $true)
$null = $Flow.WaitForInputIdle(5000)

# while ($Flow.MainWindowTitle -eq 0) 
# {
#     Start-Sleep -Milliseconds 1000
# }

$wshell = New-Object -ComObject wscript.shell;
$wshell.AppActivate('Flow.Launcher')
Start-Sleep 3

Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.SendKeys]::SendWait("% ")
[System.Windows.Forms.SendKeys]::SendWait($ActionKeyword)
