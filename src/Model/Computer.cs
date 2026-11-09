namespace WinMemoryCleaner
{
    /// <summary>
    /// Computer
    /// </summary>
    public class Computer : ObservableObject
    {
        private volatile Memory _memory;
        private readonly object _memoryDisplayGate = new object();
        private volatile bool _memoryNotificationsEnabled = true;
        private bool _memoryNotificationPending;

        /// <summary>
        /// Initializes a new instance of the <see cref="Computer" /> class.
        /// </summary>
        public Computer()
        {
            _memory = new Memory();
            OperatingSystem = new OperatingSystem();
        }

        /// <summary>
        /// Memory. Replacements notify <c>Memory</c> only, so bindings rooted at
        /// <c>Computer.Memory</c> refresh without invalidating every <c>Computer</c> binding.
        /// </summary>
        public Memory Memory
        {
            get { return _memory; }
            set
            {
                bool notify;
                lock (_memoryDisplayGate)
                {
                    // Keep the shared source current even while its main-window bindings
                    // are hidden. Tray/cleaning/overlay consumers read this same snapshot.
                    _memory = value;
                    notify = _memoryNotificationsEnabled;
                    _memoryNotificationPending = !notify;
                }
                if (notify) RaisePropertyChanged("Memory");
            }
        }

        internal bool MemoryNotificationsEnabled { get { return _memoryNotificationsEnabled; } }

        internal bool SetMemoryNotificationsEnabled(bool enabled)
        {
            bool notify;
            lock (_memoryDisplayGate)
            {
                if (_memoryNotificationsEnabled == enabled) return false;
                _memoryNotificationsEnabled = enabled;
                notify = enabled && _memoryNotificationPending;
                if (notify) _memoryNotificationPending = false;
            }
            // Do not invoke subscribers under the state lock. Restore publishes only
            // the latest snapshot, never a queue of intermediate background samples.
            if (notify) RaisePropertyChanged("Memory");
            return true;
        }

        /// <summary>
        /// Operating System
        /// </summary>
        public OperatingSystem OperatingSystem { get; set; }

    }
}
