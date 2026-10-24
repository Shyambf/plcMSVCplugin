$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
& "$ScriptDir\plcMSVCplugin\build_vsix.ps1"
