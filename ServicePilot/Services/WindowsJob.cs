using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ServicePilot.Services;

internal sealed class WindowsJob : IDisposable
{
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private IntPtr _handle;

    private WindowsJob(IntPtr handle)
    {
        _handle = handle;
    }

    public static WindowsJob CreateKillOnClose()
    {
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "创建 Windows Job 失败。");

        var info = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var length = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var infoPtr = Marshal.AllocHGlobal(length);

        try
        {
            Marshal.StructureToPtr(info, infoPtr, fDeleteOld: false);
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformationClass, infoPtr, (uint)length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "配置 Windows Job 失败。");
        }
        catch
        {
            CloseHandle(handle);
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }

        return new WindowsJob(handle);
    }

    public void Assign(Process process)
    {
        if (_handle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(WindowsJob));

        if (!AssignProcessToJobObject(_handle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "进程加入 Windows Job 失败。");
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
            CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public IntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
