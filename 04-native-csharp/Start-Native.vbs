Option Explicit

Dim shell, fso, baseDir, exePath, args, arg
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

baseDir = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = fso.BuildPath(baseDir, "UUVoiceClipboard.Native.exe")
If Not fso.FileExists(exePath) Then
  MsgBox "UUVoiceClipboard.Native.exe was not found. Keep this script beside the EXE.", vbExclamation, "UU Voice Bridge"
  WScript.Quit 1
End If

args = ""
For Each arg In WScript.Arguments
  If Len(args) > 0 Then args = args & " "
  args = args & Chr(34) & Replace(arg, Chr(34), Chr(34) & Chr(34)) & Chr(34)
Next

shell.CurrentDirectory = baseDir
If Len(args) > 0 Then args = " " & args
shell.Run Chr(34) & exePath & Chr(34) & args, 0, False
