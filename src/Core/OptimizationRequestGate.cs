using System.Threading;

namespace WinMemoryCleaner
{
    // Admission is taken before dispatch, so queued work counts as busy too.
    internal sealed class OptimizationRequestGate
    {
        private int _pending;
        internal bool TryEnter() { return Interlocked.CompareExchange(ref _pending,1,0)==0; }
        internal void Exit() { Interlocked.Exchange(ref _pending,0); }
    }
}
