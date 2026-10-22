using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WinMemoryCleaner
{
    // Membership notifications are merged; ProcessRow cell notifications remain untouched.
    internal sealed class ProcessRowCollection : ObservableCollection<ProcessRow>
    {
        private int _depth;
        private bool _changed;
        internal void BeginUpdate() { _depth++; }
        internal void EndUpdate()
        {
            if(_depth==0)throw new InvalidOperationException("No process-row batch is active.");
            _depth--;
            if(_depth!=0||!_changed)return;
            _changed=false;
            base.OnPropertyChanged(new PropertyChangedEventArgs("Count"));
            base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if(_depth!=0) { _changed=true;return; }
            base.OnCollectionChanged(e);
        }
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if(_depth!=0)return;
            base.OnPropertyChanged(e);
        }
    }
}
