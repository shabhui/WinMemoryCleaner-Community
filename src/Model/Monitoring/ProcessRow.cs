using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public sealed class ProcessRow : ObservableObject
    {
        public ProcessSnapshot Snapshot { get; private set; }
        public int Id { get { return Snapshot.Id; } }
        public string Name { get { return Snapshot.Name; } }
        public string MemoryText { get { return Bytes(Snapshot.WorkingSetBytes); } }
        public string PrivateText { get { return Bytes(Snapshot.PrivateBytes); } }
        public string CpuText { get { return Percent(Snapshot.CpuUsage); } }
        public string GpuText { get { return Percent(Snapshot.GpuUsage); } }
        public string GpuMemoryText { get { return Bytes(Snapshot.GpuMemoryBytes); } }
        public double CpuSortKey { get { return Snapshot.CpuUsage ?? -1; } }
        public double GpuSortKey { get { return Snapshot.GpuUsage ?? -1; } }
        public long MemorySortKey { get { return Snapshot.WorkingSetBytes ?? -1; } }
        public long PrivateSortKey { get { return Snapshot.PrivateBytes ?? -1; } }
        public long GpuMemorySortKey { get { return Snapshot.GpuMemoryBytes ?? -1; } }

        public ProcessRow(ProcessSnapshot snapshot) { Snapshot = snapshot; }
        public void Apply(ProcessSnapshot snapshot)
        {
            if (snapshot == null) return;
            var old = Snapshot; Snapshot = snapshot;
            if (old.Name != snapshot.Name) RaisePropertyChanged("Name");
            if (old.WorkingSetBytes != snapshot.WorkingSetBytes) { RaisePropertyChanged("MemoryText"); RaisePropertyChanged("MemorySortKey"); }
            if (old.PrivateBytes != snapshot.PrivateBytes) { RaisePropertyChanged("PrivateText"); RaisePropertyChanged("PrivateSortKey"); }
            if (old.CpuUsage != snapshot.CpuUsage) { RaisePropertyChanged("CpuText"); RaisePropertyChanged("CpuSortKey"); }
            if (old.GpuUsage != snapshot.GpuUsage) { RaisePropertyChanged("GpuText"); RaisePropertyChanged("GpuSortKey"); }
            if (old.GpuMemoryBytes != snapshot.GpuMemoryBytes) { RaisePropertyChanged("GpuMemoryText"); RaisePropertyChanged("GpuMemorySortKey"); }
        }
        private static string Bytes(long? bytes)
        {
            return bytes.HasValue ? (bytes.Value / 1048576.0).ToString("N1", CultureInfo.InvariantCulture) + " MiB" : "—";
        }
        private static string Percent(double? value)
        {
            return value.HasValue ? value.Value.ToString("F1", CultureInfo.InvariantCulture) + "%" : "—";
        }
    }

    public sealed class ProcessTable
    {
        private readonly Dictionary<int, ProcessRow> _byId = new Dictionary<int, ProcessRow>();
        public ObservableCollection<ProcessRow> Rows { get; private set; }
        public ProcessTable() { Rows = new ObservableCollection<ProcessRow>(); }
        public void Reconcile(IList<ProcessSnapshot> snapshots)
        {
            var seen = new HashSet<int>();
            if (snapshots != null)
            {
                foreach (var snapshot in snapshots)
                {
                    if (snapshot == null || !seen.Add(snapshot.Id)) continue;
                    ProcessRow row;
                    if (_byId.TryGetValue(snapshot.Id, out row))
                    {
                        if (row.Snapshot.CreationTime == snapshot.CreationTime) { row.Apply(snapshot); continue; }
                        Rows.Remove(row); _byId.Remove(snapshot.Id);
                    }
                    row = new ProcessRow(snapshot); _byId.Add(snapshot.Id, row); Rows.Add(row);
                }
            }
            for (int i = Rows.Count - 1; i >= 0; i--)
            {
                if (!seen.Contains(Rows[i].Id)) { _byId.Remove(Rows[i].Id); Rows.RemoveAt(i); }
            }
        }
        public void Clear() { Rows.Clear(); _byId.Clear(); }
    }

    public sealed class ProcessRowComparer : IComparer
    {
        private readonly string _field;
        private readonly bool _descending;
        public ProcessRowComparer(string field, bool descending) { _field = field; _descending = descending; }
        public int Compare(object x, object y)
        {
            var a = x as ProcessRow; var b = y as ProcessRow;
            if (a == null || b == null) return a == null ? (b == null ? 0 : 1) : -1;
            int result;
            if (_field == "Name") result = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            else if (_field == "Id") result = a.Id.CompareTo(b.Id);
            else
            {
                double av = Key(a), bv = Key(b);
                // Missing readings remain after real idle zeros in either direction.
                if (av < 0 || bv < 0) return av < 0 ? (bv < 0 ? a.Id.CompareTo(b.Id) : 1) : -1;
                result = av.CompareTo(bv);
            }
            if (_descending) result = -result;
            return result == 0 ? a.Id.CompareTo(b.Id) : result;
        }
        private double Key(ProcessRow row)
        {
            if (_field == "CpuUsage") return row.CpuSortKey;
            if (_field == "GpuUsage") return row.GpuSortKey;
            if (_field == "GpuMemoryBytes") return row.GpuMemorySortKey;
            if (_field == "PrivateBytes") return row.PrivateSortKey;
            return row.MemorySortKey;
        }
    }
}
#pragma warning restore 1591
