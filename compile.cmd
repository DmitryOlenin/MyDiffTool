@echo off
"c:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe" /target:winexe /win32icon:app.ico /out:DiffTool.exe DiffTool.cs