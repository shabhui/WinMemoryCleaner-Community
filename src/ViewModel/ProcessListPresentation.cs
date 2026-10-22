using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;

namespace WinMemoryCleaner
{
    // UI-only coordination. Local queries never request sampling or create a resident timer.
    internal sealed class ProcessListPresentation : ObservableObject, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private readonly MonitorText _labels;
        private readonly ProcessTable _table=new ProcessTable();
        private readonly ProcessPresentationState _state=new ProcessPresentationState();
        private DispatcherTimer _timer;
        private EventHandler _queryTick;
        private int _revision;
        private string _query=string.Empty,_committedQuery=string.Empty,_pendingQuery;
        private string _sortField="WorkingSetBytes";
        private bool _descending=true,_disposed,_changing,_reportedCanAct;
        private int _reportedTotal,_reportedMatched;
        private string _countText="—",_statusText=string.Empty,_statusDetail=string.Empty;
        private ProcessRow _selected;

        internal ProcessListPresentation(Dispatcher dispatcher,MonitorText labels)
        {
            if(dispatcher==null)throw new ArgumentNullException("dispatcher");
            if(labels==null)throw new ArgumentNullException("labels");
            _dispatcher=dispatcher;_labels=labels;
            View=new ListCollectionView(_table.Rows);
            View.Filter=delegate(object value) { var row=(ProcessRow)value;return Matches(row); };
            View.CustomSort=new ProcessRowComparer(_sortField,_descending);
        }
        internal ListCollectionView View { get; private set; }
        internal string SearchQuery
        {
            get { return _query; }
            set
            {
                if(_disposed)return;
                string raw=value??string.Empty;
                if(string.Equals(_query,raw,StringComparison.Ordinal))return;
                _query=raw;
                string effective=raw.Trim();
                if(!_state.IsActive)
                { CancelQuery();_committedQuery=effective; }
                else if(Equivalent(effective,_committedQuery))CancelQuery();
                else if(effective.Length==0) { CancelQuery();SubmitQuery(effective); }
                else if(!Equivalent(effective,_pendingQuery))ScheduleQuery(effective);
                RaisePropertyChanged("SearchQuery");RaisePropertyChanged("CanClearSearch");RefreshText();
            }
        }
        internal ProcessRow SelectedProcess
        {
            get { return _selected; }
            set
            {
                var selected=IsVisibleRow(value)?value:null;
                if(ReferenceEquals(_selected,selected))return;
                _selected=selected;RaisePropertyChanged("SelectedProcess");NotifyActionAvailability();
            }
        }
        internal int TotalCount { get { return _state.TotalCount; } }
        internal int MatchedCount { get { return _state.MatchCount; } }
        internal bool CanClearSearch { get { return !_disposed&&_query.Length!=0; } }
        internal bool CanActOnSelectedProcess { get { return _selected!=null&&IsCurrentTarget(_selected.Snapshot); } }
        internal string CountText { get { return _countText; } }
        internal string StatusText { get { return _statusText; } }
        internal string StatusDetail { get { return _statusDetail; } }
        internal bool HasStatus { get { return _statusText.Length!=0; } }

        internal void SetActive(bool active)
        {
            if(_disposed||_state.IsActive==active)return;
            CancelQuery();_state.SetActive(active);SelectedProcess=null;
            if(!active)_table.Clear();
            else _committedQuery=_query.Trim(); // Retained textbox input belongs to the new session, not an old pending callback.
            RefreshText();
        }
        internal void Apply(ResourceSnapshot snapshot,string failure)
        {
            if(_disposed||!_state.IsActive)return;
            if(snapshot==null)
            {
                _state.RejectSnapshot(failure);SelectedProcess=null;RefreshText();return;
            }
            var retained=_state.HasFailure?null:_selected;
            if(_state.HasFailure)SelectedProcess=null;
            _changing=true;
            try
            {
                var changes=_table.ReconcileBatch(snapshot.Processes);
                // A merged membership Reset has already rebuilt the actual view with all changed cell values.
                if((changes&ProcessTableChange.Membership)==0&&ProcessTableChanges.RequiresRefresh(changes,_sortField,_committedQuery.Length!=0))View.Refresh();
                _state.AcceptSnapshot(_table.Rows.Count,View.Count);
                SelectedProcess=retained;
            }
            finally { _changing=false; }
            RefreshText();
        }
        internal void ClearSearch() { SearchQuery=string.Empty; }
        internal ListSortDirection Sort(string field)
        {
            if(_disposed)return _descending?ListSortDirection.Descending:ListSortDirection.Ascending;
            var retained=_selected;
            _descending=_sortField==field?!_descending:field!="Name"&&field!="Id";
            _sortField=field;View.CustomSort=new ProcessRowComparer(field,_descending);
            SelectedProcess=retained;_state.UpdateCounts(_table.Rows.Count,View.Count);RefreshText();
            return _descending?ListSortDirection.Descending:ListSortDirection.Ascending;
        }
        internal bool IsCurrentTarget(ProcessSnapshot target)
        {
            if(_disposed||_changing||!_state.CanUseCurrentData||target==null||target.Id<=0||target.CreationTime<=0)return false;
            var row=_table.Find(target.Id);
            return row!=null&&row.Snapshot.CreationTime==target.CreationTime&&Matches(row);
        }
        private bool IsVisibleRow(ProcessRow row)
        { return !_disposed&&_state.IsActive&&row!=null&&ReferenceEquals(_table.Find(row.Id),row)&&Matches(row); }
        private bool Matches(ProcessRow row) { return MonitoringPolicy.MatchesProcess(row.Name,row.Id,_committedQuery); }
        private static bool Equivalent(string left,string right) { return string.Equals(left,right,StringComparison.OrdinalIgnoreCase); }
        private void ScheduleQuery(string effective)
        {
            CancelQuery();_pendingQuery=effective;_state.SetFiltering(true);
            int revision=_revision;
            _timer=new DispatcherTimer(DispatcherPriority.Background,_dispatcher) { Interval=TimeSpan.FromMilliseconds(150) };
            _queryTick=delegate { CommitQuery(revision); };
            _timer.Tick+=_queryTick;_timer.Start();
        }
        private void CancelQuery()
        {
            _revision++;
            if(_timer!=null) { _timer.Stop();_timer.Tick-=_queryTick;_timer=null;_queryTick=null; }
            _pendingQuery=null;_state.SetFiltering(false);
        }
        internal void CommitQuery(int revision)
        {
            if(_disposed||!_state.IsActive||revision!=_revision||_pendingQuery==null)return;
            string effective=_pendingQuery;CancelQuery();SubmitQuery(effective);RefreshText();
        }
        private void SubmitQuery(string effective)
        {
            if(Equivalent(effective,_committedQuery))return;
            var retained=_selected;_committedQuery=effective;
            View.Refresh();SelectedProcess=retained;_state.UpdateCounts(_table.Rows.Count,View.Count);
        }
        internal void RefreshText()
        {
            bool hadStatus=HasStatus;
            string count=_state.HasSnapshot?string.Format(Localizer.Culture,_labels["ProcessMatches"],MatchedCount,TotalCount):"—";
            string status=string.Empty;
            switch(_state.Status)
            {
                case ProcessListStatus.Loading:status=_labels["ProcessLoading"];break;
                case ProcessListStatus.Empty:status=_labels["ProcessEmpty"];break;
                case ProcessListStatus.NoMatches:status=_labels["ProcessNoMatches"];break;
                case ProcessListStatus.Filtering:status=_labels["ProcessFiltering"];break;
                case ProcessListStatus.Failed:status=_labels["ProcessFailed"];break;
                case ProcessListStatus.Stale:status=_labels["ProcessStale"];break;
            }
            SetText(ref _countText,count,"CountText");SetText(ref _statusText,status,"StatusText");
            SetText(ref _statusDetail,_state.HasFailure?_state.FailureDetail:string.Empty,"StatusDetail");
            if(hadStatus!=HasStatus)RaisePropertyChanged("HasStatus");
            if(_reportedTotal!=TotalCount) { _reportedTotal=TotalCount;RaisePropertyChanged("TotalCount"); }
            if(_reportedMatched!=MatchedCount) { _reportedMatched=MatchedCount;RaisePropertyChanged("MatchedCount"); }
            NotifyActionAvailability();
        }
        private void SetText(ref string current,string value,string property)
        { if(string.Equals(current,value,StringComparison.Ordinal))return;current=value;RaisePropertyChanged(property); }
        private void NotifyActionAvailability()
        {
            bool current=CanActOnSelectedProcess;
            if(current==_reportedCanAct)return;
            _reportedCanAct=current;RaisePropertyChanged("CanActOnSelectedProcess");
        }
        public void Dispose()
        {
            if(_disposed)return;
            SetActive(false);_disposed=true;CancelQuery();RaisePropertyChanged("CanClearSearch");
            GC.SuppressFinalize(this);
        }
    }
}
