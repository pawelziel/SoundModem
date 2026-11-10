using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SoundModem.Transport;

internal static class SerialTransport
{
    public static async Task<ISerialTransport> OpenAsync(string endpoint, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeServerStream(
                endpoint,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            Console.Error.WriteLine($"Waiting for a bridge to connect to named pipe '{endpoint}'...");
            await pipe.WaitForConnectionAsync(cancellationToken);
            return new PipeTransport(pipe, endpoint);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return await PtyTransport.OpenAsync(endpoint, cancellationToken);
        }

        throw new PlatformNotSupportedException("Only Linux, macOS, and Windows are supported.");
    }

    private sealed class PipeTransport(NamedPipeServerStream pipe, string endpoint) : ISerialTransport
    {
        public Stream Stream => pipe;
        public string Endpoint => $"\\\\.\\pipe\\{endpoint}";
        public ValueTask DisposeAsync() => pipe.DisposeAsync();
    }

    private sealed class PtyTransport : ISerialTransport
    {
        private readonly FileStream _masterStream;
        private readonly SafeFileHandle _slaveHandle;
        private readonly string _linkPath;
        private readonly string _slavePath;

        private PtyTransport(FileStream masterStream, SafeFileHandle slaveHandle, string linkPath, string slavePath)
        {
            _masterStream = masterStream;
            _slaveHandle = slaveHandle;
            _linkPath = linkPath;
            _slavePath = slavePath;
        }

        public Stream Stream => _masterStream;
        public string Endpoint => _linkPath;

        public static async Task<PtyTransport> OpenAsync(string endpoint, CancellationToken cancellationToken)
        {
            var noControllingTerminal = OperatingSystem.IsMacOS() ? 0x200 : 0x100;
            var masterFd = PtyNative.OpenPseudoTerminal(0x2 | noControllingTerminal);
            if (masterFd < 0)
            {
                throw new IOException($"Could not open a PTY master (errno {Marshal.GetLastPInvokeError()}).");
            }

            SafeFileHandle? masterHandle = new(new IntPtr(masterFd), ownsHandle: true);
            SafeFileHandle? slaveHandle = null;
            FileStream? masterStream = null;
            var linkPath = Path.GetFullPath(endpoint);
            var linkCreated = false;

            try
            {
                if (PtyNative.GrantPseudoTerminal(masterFd) != 0 || PtyNative.UnlockPseudoTerminal(masterFd) != 0)
                {
                    throw new IOException($"Could not prepare the PTY (errno {Marshal.GetLastPInvokeError()}).");
                }

                var slavePointer = PtyNative.GetSlaveName(masterFd);
                var slavePath = Marshal.PtrToStringUTF8(slavePointer)
                    ?? throw new IOException("Could not determine the PTY slave path.");
                var slaveFd = PtyNative.OpenSlave(slavePath, 0x2 | noControllingTerminal);
                if (slaveFd < 0)
                {
                    throw new IOException($"Could not open the PTY slave '{slavePath}' (errno {Marshal.GetLastPInvokeError()}).");
                }

                slaveHandle = new SafeFileHandle(new IntPtr(slaveFd), ownsHandle: true);
                await ConfigureRawTerminalAsync(slavePath, cancellationToken);

                if (File.Exists(linkPath) || Directory.Exists(linkPath))
                {
                    throw new IOException($"The PTY endpoint already exists: {linkPath}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
                File.CreateSymbolicLink(linkPath, slavePath);
                linkCreated = true;
                masterStream = new FileStream(masterHandle, FileAccess.ReadWrite, 4096, isAsync: false);
                return new PtyTransport(masterStream, slaveHandle, linkPath, slavePath);
            }
            catch
            {
                masterStream?.Dispose();
                masterHandle?.Dispose();
                slaveHandle?.Dispose();
                if (linkCreated)
                {
                    File.Delete(linkPath);
                }

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _masterStream.DisposeAsync();
            _slaveHandle.Dispose();
            if (File.Exists(_linkPath) &&
                string.Equals(new FileInfo(_linkPath).LinkTarget, _slavePath, StringComparison.Ordinal))
            {
                File.Delete(_linkPath);
            }
        }

        private static async Task ConfigureRawTerminalAsync(string slavePath, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo("stty")
            {
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(OperatingSystem.IsMacOS() ? "-f" : "-F");
            startInfo.ArgumentList.Add(slavePath);
            foreach (var argument in new[] { "raw", "-echo", "-ixon", "-ixoff" })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)
                ?? throw new IOException("Could not start stty to configure the PTY.");
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                throw new IOException($"Could not configure the PTY with stty: {await errorTask}");
            }
        }
    }

    private static class PtyNative
    {
        [DllImport("libc", SetLastError = true, EntryPoint = "posix_openpt")]
        internal static extern int OpenPseudoTerminal(int flags);

        [DllImport("libc", SetLastError = true, EntryPoint = "grantpt")]
        internal static extern int GrantPseudoTerminal(int fileDescriptor);

        [DllImport("libc", SetLastError = true, EntryPoint = "unlockpt")]
        internal static extern int UnlockPseudoTerminal(int fileDescriptor);

        [DllImport("libc", SetLastError = true, EntryPoint = "ptsname")]
        internal static extern IntPtr GetSlaveName(int fileDescriptor);

        [DllImport("libc", SetLastError = true, EntryPoint = "open")]
        internal static extern int OpenSlave(string path, int flags);
    }
}