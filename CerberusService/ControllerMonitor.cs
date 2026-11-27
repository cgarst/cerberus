using System;
using System.Collections.Generic;

namespace RoomAgent
{
    public interface IControllerMonitor
    {
        IList<string> PollNewlyConnectedControllers();
    }

    /// <summary>
    /// Detects XInput controllers coming online as children of a USB Xbox Wireless Adapter,
    /// ignoring controllers connected over Bluetooth or direct USB.
    /// </summary>
    public class XboxReceiverControllerMonitor : IControllerMonitor
    {
        private readonly Func<IList<string>> _readConnectedIds;
        private readonly object _sync = new object();
        private HashSet<string> _knownConnectedIds;

        public XboxReceiverControllerMonitor(string[] receiverDevicePrefixes)
            : this(() => NativeInterop.GetConnectedXboxReceiverControllerIds(receiverDevicePrefixes))
        {
        }

        internal XboxReceiverControllerMonitor(Func<IList<string>> readConnectedIds)
        {
            if (readConnectedIds == null)
            {
                throw new ArgumentNullException("readConnectedIds");
            }
            _readConnectedIds = readConnectedIds;
            _knownConnectedIds = ToSet(_readConnectedIds());
        }

        public IList<string> PollNewlyConnectedControllers()
        {
            HashSet<string> current = ToSet(_readConnectedIds());
            List<string> newlyConnected = new List<string>();

            lock (_sync)
            {
                foreach (string id in current)
                {
                    if (!_knownConnectedIds.Contains(id))
                    {
                        newlyConnected.Add(id);
                    }
                }
                _knownConnectedIds = current;
            }

            return newlyConnected;
        }

        private static HashSet<string> ToSet(IList<string> ids)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(ids[i]))
                    {
                        set.Add(ids[i]);
                    }
                }
            }
            return set;
        }
    }
}
