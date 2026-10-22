namespace WinMemoryCleaner
{
    internal enum ProcessListStatus { Inactive, Loading, Ready, Empty, NoMatches, Filtering, Failed, Stale }

    // Process-list success is independent of unavailable individual resource metrics.
    internal sealed class ProcessPresentationState
    {
        internal bool IsActive { get; private set; }
        internal bool HasSnapshot { get; private set; }
        internal bool HasFailure { get; private set; }
        internal bool IsFiltering { get; private set; }
        internal string FailureDetail { get; private set; }
        internal int TotalCount { get; private set; }
        internal int MatchCount { get; private set; }
        internal ProcessPresentationState() { FailureDetail=string.Empty; }
        internal ProcessListStatus Status
        {
            get
            {
                if(!IsActive)return ProcessListStatus.Inactive;
                if(HasFailure)return HasSnapshot?ProcessListStatus.Stale:ProcessListStatus.Failed;
                if(!HasSnapshot)return ProcessListStatus.Loading;
                if(IsFiltering)return ProcessListStatus.Filtering;
                if(TotalCount==0)return ProcessListStatus.Empty;
                return MatchCount==0?ProcessListStatus.NoMatches:ProcessListStatus.Ready;
            }
        }
        internal bool CanUseCurrentData { get { return IsActive&&HasSnapshot&&!HasFailure&&!IsFiltering; } }
        internal void SetActive(bool active)
        {
            if(IsActive==active)return;
            IsActive=active;HasSnapshot=HasFailure=IsFiltering=false;
            FailureDetail=string.Empty;TotalCount=MatchCount=0;
        }
        internal void SetFiltering(bool filtering) { if(IsActive)IsFiltering=filtering; }
        internal void AcceptSnapshot(int total,int matched)
        {
            if(!IsActive)return;
            HasSnapshot=true;HasFailure=false;FailureDetail=string.Empty;
            UpdateCounts(total,matched);
        }
        internal void UpdateCounts(int total,int matched)
        {
            if(!IsActive||!HasSnapshot)return;
            TotalCount=total;MatchCount=matched;
        }
        internal void RejectSnapshot(string detail)
        {
            if(!IsActive)return;
            HasFailure=true;FailureDetail=detail??string.Empty;
        }
    }
}
