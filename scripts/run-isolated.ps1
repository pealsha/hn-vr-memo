param(
    [string]$Script = (Join-Path $PSScriptRoot 'verify.ps1'),
    [string]$Log = (Join-Path $PSScriptRoot '../artifacts/foundation/verify.log'),
    [string[]]$ScriptArguments = @()
)
$ErrorActionPreference = 'Stop'
$scriptPath = (Resolve-Path -LiteralPath $Script).Path
$logPath = [System.IO.Path]::GetFullPath($Log)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($logPath)) | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class IsolatedDesktopRunner
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string reserved, desktop, title;
        public int x, y, xSize, ySize, xCountChars, yCountChars, fillAttribute, flags;
        public short showWindow, reserved2Count;
        public IntPtr reserved2, stdInput, stdOutput, stdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr process, thread;
        public int processId, threadId;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devMode, int flags, uint access, IntPtr attributes);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string currentDirectory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    public static int Run(string executable, string arguments, string workingDirectory)
    {
        string name = "HnVRMemoVerify-" + Guid.NewGuid().ToString("N");
        IntPtr desktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateDesktop failed");
        try
        {
            var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)), desktop = name };
            var command = new StringBuilder("\"" + executable + "\" " + arguments);
            ProcessInformation process;
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, workingDirectory, ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed");
            try
            {
                if (WaitForSingleObject(process.process, 0xffffffff) != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Wait failed");
                uint code;
                if (!GetExitCodeProcess(process.process, out code)) throw new Win32Exception(Marshal.GetLastWin32Error(), "GetExitCodeProcess failed");
                return unchecked((int)code);
            }
            finally { CloseHandle(process.thread); CloseHandle(process.process); }
        }
        finally { CloseDesktop(desktop); }
    }
}
'@
$quotedScript = $scriptPath.Replace("'", "''")
$quotedLog = $logPath.Replace("'", "''")
$arguments = ($ScriptArguments | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ' '
$childCommand = "& powershell -NoProfile -ExecutionPolicy Bypass -File '$quotedScript' $arguments *> '$quotedLog'; exit `$LASTEXITCODE"
$encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($childCommand))
$exe = (Get-Command powershell.exe).Source
$exitCode = [IsolatedDesktopRunner]::Run($exe, "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded", (Get-Location).Path)
Write-Host "Isolated desktop log: $logPath"
exit $exitCode
