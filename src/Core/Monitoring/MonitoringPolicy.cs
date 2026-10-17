using System;
using System.Globalization;

#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public struct MonitoringDemand
    {
        public bool CpuGpu { get; private set; }
        public bool Processes { get; private set; }
        public bool Temperatures { get; private set; }

        public MonitoringDemand(bool cpuGpu, bool processes, bool temperatures) : this()
        {
            CpuGpu = cpuGpu;
            Processes = processes;
            Temperatures = temperatures;
        }
        public static bool operator ==(MonitoringDemand left, MonitoringDemand right) { return left.Equals(right); }
        public static bool operator !=(MonitoringDemand left, MonitoringDemand right) { return !left.Equals(right); }
        public override bool Equals(object obj)
        {
            if (!(obj is MonitoringDemand)) return false;
            var other = (MonitoringDemand)obj;
            return CpuGpu == other.CpuGpu && Processes == other.Processes && Temperatures == other.Temperatures;
        }
        public bool Equals(MonitoringDemand other) { return CpuGpu == other.CpuGpu && Processes == other.Processes && Temperatures == other.Temperatures; }
        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 31 + CpuGpu.GetHashCode();
            hash = hash * 31 + Processes.GetHashCode();
            hash = hash * 31 + Temperatures.GetHashCode();
            return hash;
        }
    }

    public static class MonitoringPolicy
    {
        public static MonitoringDemand GetDemand(bool mainVisible, bool processPage, bool overlayEnabled, bool temperaturesVisible)
        {
            bool metrics = mainVisible || overlayEnabled;
            return new MonitoringDemand(metrics, mainVisible && processPage, metrics && temperaturesVisible);
        }

        public static int NormalizeRefresh(int interval)
        {
            return interval == 1 || interval == 5 ? interval : 2;
        }

        public static bool IsCurrent(int sampledGeneration, int currentGeneration, bool disposed)
        {
            return !disposed && sampledGeneration == currentGeneration;
        }

        public static bool MatchesProcess(string name, int id, string query)
        {
            query = (query ?? string.Empty).Trim();
            return query.Length == 0 || (name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.ToString(CultureInfo.InvariantCulture).IndexOf(query, StringComparison.Ordinal) >= 0;
        }
    }
}
#pragma warning restore 1591
