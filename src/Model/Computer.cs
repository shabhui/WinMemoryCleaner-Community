namespace WinMemoryCleaner
{
    /// <summary>
    /// Computer
    /// </summary>
    public class Computer : ObservableObject
    {
        private Memory _memory;

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
                _memory = value;
                RaisePropertyChanged("Memory");
            }
        }

        /// <summary>
        /// Operating System
        /// </summary>
        public OperatingSystem OperatingSystem { get; set; }

    }
}
