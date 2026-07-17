// CpuMonitor.cs — system-wide CPU usage via GetSystemTimes (no perf-counter dependency).
using System.Runtime.InteropServices;

namespace RunCatNeo.Win;

public sealed class CpuMonitor
{
    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _primed;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    /// <summary>Returns CPU usage 0–100 since the previous call. First call returns 0.</summary>
    public float Sample()
    {
        if (!GetSystemTimes(out var idleL, out var kernelL, out var userL)) return 0f;
        ulong idle = (ulong)idleL, kernel = (ulong)kernelL, user = (ulong)userL;

        float usage = 0f;
        if (_primed)
        {
            var idleDelta = idle - _prevIdle;
            // Kernel time includes idle time, so total = kernel + user.
            var totalDelta = (kernel - _prevKernel) + (user - _prevUser);
            if (totalDelta > 0)
            {
                usage = 100f * (1f - (float)idleDelta / totalDelta);
            }
        }
        (_prevIdle, _prevKernel, _prevUser) = (idle, kernel, user);
        _primed = true;
        return Math.Clamp(usage, 0f, 100f);
    }

    /// <summary>
    /// Runner speed formula from RunnerService.updateRunnerSpeed:
    /// cpuValue = clamp(cpu / 5, 1, 20); speed = slowerUnderLoad ? 0.5 * (21 - cpuValue) : cpuValue.
    /// Frame interval is 500ms / speed (base animation is 2 fps, scaled by speed).
    /// </summary>
    public static float SpeedFor(float cpuPercent, bool slowerUnderLoad)
    {
        var cpuValue = Math.Clamp(cpuPercent / 5f, 1f, 20f);
        return slowerUnderLoad ? 0.5f * (21f - cpuValue) : cpuValue;
    }
}
