using System;

namespace RoomAgent
{
    public interface IIdleMonitor
    {
        void Sample();
        TimeSpan GetIdleDuration();
        void Reset();
    }

    public class WindowsIdleMonitor : IIdleMonitor
    {
        private readonly object _sync = new object();
        private uint[] _lastPackets;
        private DateTime _lastGamepadActivityUtc;

        public WindowsIdleMonitor()
        {
            _lastPackets = NativeInterop.GetXInputPacketNumbers();
            _lastGamepadActivityUtc = DateTime.UtcNow;
        }

        public void Sample()
        {
            uint[] current = NativeInterop.GetXInputPacketNumbers();
            lock (_sync)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (current[i] != 0 && current[i] != _lastPackets[i])
                    {
                        _lastGamepadActivityUtc = DateTime.UtcNow;
                    }
                    _lastPackets[i] = current[i];
                }
            }
        }

        public TimeSpan GetIdleDuration()
        {
            Sample();
            TimeSpan mkIdle = NativeInterop.GetMouseKeyboardIdleTime();
            TimeSpan gpIdle;
            lock (_sync)
            {
                gpIdle = DateTime.UtcNow - _lastGamepadActivityUtc;
            }
            return mkIdle < gpIdle ? mkIdle : gpIdle;
        }

        public void Reset()
        {
            lock (_sync)
            {
                _lastPackets = NativeInterop.GetXInputPacketNumbers();
                _lastGamepadActivityUtc = DateTime.UtcNow;
            }
        }
    }
}
