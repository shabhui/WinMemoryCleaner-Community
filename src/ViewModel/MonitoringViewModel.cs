using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;
using System.Globalization;
using System.Threading;
using System.Windows.Data;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public sealed class DisplayChoice : ObservableObject
    {
        private string _name;
        public string Id { get; private set; }
        public string Name
        {
            get { return _name; }
            internal set { if(_name==value)return; _name=value; RaisePropertyChanged("Name"); }
        }
        public DisplayChoice(string id, string name) { Id=id; _name=name; }
    }

    public sealed class MonitoringViewModel : ObservableObject, IDisposable
    {
        private readonly MainViewModel _cleaner;
        private readonly Dispatcher _dispatcher;
        private readonly MonitoringPump _pump;
        private readonly MonitoringSession _session;
        private readonly Func<Action,SensorDriverInstaller> _createInstaller;
        private readonly Func<IFrameRateMonitor> _createFps;
        private volatile IFrameRateMonitor _fps;
        private volatile bool _fpsActive;
        private int _fpsGeneration;
        private bool _fpsFailed;
        private ProcessSnapshot _manualFpsTarget;
        private FrameRateReading _lastFps;
        private SensorDriverInstaller _installer;
        private DispatcherTimer _installTimer;
        private readonly ProcessListPresentation _processes;
        private MonitoringOptions _options;
        private ResourceSnapshot _last;
        private bool _mainVisible, _processPage;
        private volatile bool _disposed;
        private int _pending;
        private readonly object _publicationGate = new object();
        private SamplePublication _publication;
        private int _sampleCount, _processSampleCount, _temperatureSampleCount;
        private string _failure, _actionStatus=string.Empty;
        private string _actionKey, _actionPrefix, _actionDetail;

        private sealed class SamplePublication
        {
            internal ResourceSnapshot Snapshot;
            internal FrameRateReading Fps;
            internal MonitoringDemand Demand;
            internal string Failure;
            internal int Generation, FpsGeneration;
        }

        // Publish a complete display snapshot before notifying any binding. Getters then
        // reuse formatted text, and sub-precision changes do not trigger WPF layout work.
        private struct DisplayReadings
        {
            internal string CpuText, GpuText, CpuTemperatureText, GpuTemperatureText;
            internal string OverlayCpuText, OverlayGpuText, OverlayCpuTemperatureText, OverlayGpuTemperatureText;
            internal string CpuDetail, GpuDetail, CpuTemperatureDetail, GpuTemperatureDetail;
            internal string GpuName, MemoryText;
            internal string FpsText, OverlayFpsText, FpsDetail, FpsTargetText;
        }
        private static readonly IList<int> _refreshChoices = Array.AsReadOnly(new[] { 1, 2, 5 });
        private DisplayReadings _readings;
        private string _sensorStatusText, _sensorStatusDetail;
        private bool _reportedInstalling, _reportedCanInstall, _reportedCanCancel;

        public MonitoringViewModel(MainViewModel cleaner, Dispatcher dispatcher)
            : this(cleaner,dispatcher,delegate { return new ResourceMonitor(); }) { }
        internal MonitoringViewModel(MainViewModel cleaner,Dispatcher dispatcher,Func<IResourceMonitor> createMonitor)
            : this(cleaner,dispatcher,createMonitor,delegate(Action completed) { return new SensorDriverInstaller(completed); },delegate { return new FrameRateMonitor(); }) { }
        internal MonitoringViewModel(MainViewModel cleaner,Dispatcher dispatcher,Func<IResourceMonitor> createMonitor,Func<Action,SensorDriverInstaller> createInstaller)
            : this(cleaner,dispatcher,createMonitor,createInstaller,delegate { return new FrameRateMonitor(); }) { }
        internal MonitoringViewModel(MainViewModel cleaner,Dispatcher dispatcher,Func<IResourceMonitor> createMonitor,Func<IFrameRateMonitor> createFps)
            : this(cleaner,dispatcher,createMonitor,delegate(Action completed) { return new SensorDriverInstaller(completed); },createFps) { }
        private MonitoringViewModel(MainViewModel cleaner,Dispatcher dispatcher,Func<IResourceMonitor> createMonitor,Func<Action,SensorDriverInstaller> createInstaller,Func<IFrameRateMonitor> createFps)
        {
            if(cleaner==null)throw new ArgumentNullException("cleaner");
            if(dispatcher==null)throw new ArgumentNullException("dispatcher");
            if(createInstaller==null)throw new ArgumentNullException("createInstaller");
            if(createFps==null)throw new ArgumentNullException("createFps");
            _cleaner=cleaner; _dispatcher=dispatcher; _options=Settings.Monitoring;
            _createInstaller=createInstaller;_createFps=createFps;_session=new MonitoringSession(createMonitor);
            _options.Normalize(); Labels=new MonitorText();
            _processes=new ProcessListPresentation(dispatcher,Labels);
            _processes.PropertyChanged+=OnProcessPresentationChanged;
            _pump=new MonitoringPump(Sample, Suspend);
            Localizer.StaticPropertyChanged+=OnLanguageChanged;
            RefreshDisplays();
            RefreshReadings();
        }
        public MonitorText Labels { get; private set; }
        public ListCollectionView ProcessesView { get { return _processes.View; } }
        public IList<DisplayChoice> Displays { get; private set; }
        public IList<int> RefreshChoices { get { return _refreshChoices; } }
        public int OverlayLayout
        {
            get { return _options.Layout; }
            set
            {
                if(value<0||value>1)return; // Ignore ComboBox's transient cleared selection during translation.
                int layout=value;
                if(_options.Layout==layout)return;
                _options.Layout=layout; SaveOption("OverlayLayout",false); RaisePropertyChanged("OverlayOrientation");
            }
        }
        public Orientation OverlayOrientation { get { return _options.Layout==1?Orientation.Vertical:Orientation.Horizontal; } }
        public int RefreshSeconds { get { return _options.RefreshSeconds; } set { _options.RefreshSeconds=MonitoringPolicy.NormalizeRefresh(value); SaveOption("RefreshSeconds",true); } }
        public bool OverlayEnabled { get { return _options.OverlayEnabled; } set { if(_options.OverlayEnabled==value)return; _options.OverlayEnabled=value; SaveOption("OverlayEnabled",true); } }
        public bool ClickThrough { get { return _options.ClickThrough; } set { _options.ClickThrough=value; SaveOption("ClickThrough",false); } }
        public string OverlayScreen { get { return _options.Screen; } set { _options.Screen=value??string.Empty; SaveOption("OverlayScreen",false); } }
        public int OverlayCorner { get { return _options.Corner; } set { if(value<0||value>4||_options.Corner==value)return; _options.Corner=value; SaveOption("OverlayCorner",false); } }
        public double OverlayX { get { return _options.X; } }
        public double OverlayY { get { return _options.Y; } }
        public bool ShowCpu { get { return Item(1); } set { SetItem(1,value,"ShowCpu"); } }
        public bool ShowGpu { get { return Item(2); } set { SetItem(2,value,"ShowGpu"); } }
        public bool ShowMemory { get { return Item(4); } set { SetItem(4,value,"ShowMemory"); } }
        public bool ShowCpuTemperature { get { return Item(8); } set { SetItem(8,value,"ShowCpuTemperature"); } }
        public bool ShowGpuTemperature { get { return Item(16); } set { SetItem(16,value,"ShowGpuTemperature"); } }
        public bool ShowFps { get { return Item(32); } set { if(ShowFps!=value)SetItem(32,value,"ShowFps"); } }
        public int FpsTargetMode
        {
            get { return _options.FpsTargetMode; }
            set
            {
                if(value<0||value>1||_options.FpsTargetMode==value)return; // Ignore transient ComboBox clearing during translation.
                _options.FpsTargetMode=value;SaveOption("FpsTargetMode",false);ChangeFpsTarget();
            }
        }
        public bool HasFpsTarget { get { return _manualFpsTarget!=null; } }
        public bool ShowCpuGroup { get { return ShowCpu||ShowCpuTemperature; } }
        public bool ShowGpuGroup { get { return ShowGpu||ShowGpuTemperature; } }
        public string SearchQuery { get { return _processes.SearchQuery; } set { _processes.SearchQuery=value; } }
        public ProcessRow SelectedProcess { get { return _processes.SelectedProcess; } set { _processes.SelectedProcess=value; } }
        public int ProcessCount { get { return _processes.TotalCount; } }
        public string ProcessCountText { get { return _processes.CountText; } }
        public bool CanClearSearch { get { return _processes.CanClearSearch; } }
        public bool CanActOnSelectedProcess { get { return _processes.CanActOnSelectedProcess; } }
        public bool CanUseSelectedFpsTarget { get { return !_disposed&&CanActOnSelectedProcess; } }
        public string ProcessStatusText { get { return _processes.StatusText; } }
        public string ProcessStatusDetail { get { return _processes.StatusDetail; } }
        public bool HasProcessStatus { get { return _processes.HasStatus; } }
        public void ClearSearch() { _processes.ClearSearch(); }
        internal bool IsCurrentProcessTarget(ProcessSnapshot target) { return _processes.IsCurrentTarget(target); }
        internal bool UseSelectedFpsTarget()
        {
            if(!CanUseSelectedFpsTarget)return false;
            ProcessSnapshot source=SelectedProcess.Snapshot;
            if(!IsCurrentProcessTarget(source))return false;
            // Retain only an immutable-by-ownership identity copy, never a mutable table row or Process handle.
            _manualFpsTarget=CopyFpsTarget(source);
            if(_options.FpsTargetMode!=1) { _options.FpsTargetMode=1;SaveOption("FpsTargetMode",false); }
            RaisePropertyChanged("HasFpsTarget");ChangeFpsTarget();return true;
        }
        internal void ClearFpsTarget()
        {
            if(_disposed||_manualFpsTarget==null)return;
            _manualFpsTarget=null;RaisePropertyChanged("HasFpsTarget");ChangeFpsTarget();
        }
        private static ProcessSnapshot CopyFpsTarget(ProcessSnapshot source)
        { return source==null?null:new ProcessSnapshot { Id=source.Id,CreationTime=source.CreationTime,Name=source.Name }; }
        private void OnProcessPresentationChanged(object sender,PropertyChangedEventArgs e)
        {
            string property=e.PropertyName;
            if(property=="TotalCount")property="ProcessCount";
            else if(property=="CountText")property="ProcessCountText";
            else if(property=="StatusText")property="ProcessStatusText";
            else if(property=="StatusDetail")property="ProcessStatusDetail";
            else if(property=="HasStatus")property="HasProcessStatus";
            else if(property=="MatchedCount")return;
            RaisePropertyChanged(property);
            if(property=="SelectedProcess"||property=="CanActOnSelectedProcess")RaisePropertyChanged("CanUseSelectedFpsTarget");
        }
        public int SampleCount { get { return _sampleCount; } }
        public int ProcessSampleCount { get { return _processSampleCount; } }
        public int TemperatureSampleCount { get { return _temperatureSampleCount; } }
        public string CpuText { get { return _readings.CpuText; } }
        public string GpuText { get { return _readings.GpuText; } }
        public string CpuTemperatureText { get { return _readings.CpuTemperatureText; } }
        public string GpuTemperatureText { get { return _readings.GpuTemperatureText; } }
        public string OverlayCpuText { get { return _readings.OverlayCpuText; } }
        public string OverlayGpuText { get { return _readings.OverlayGpuText; } }
        public string OverlayCpuTemperatureText { get { return _readings.OverlayCpuTemperatureText; } }
        public string OverlayGpuTemperatureText { get { return _readings.OverlayGpuTemperatureText; } }
        public string CpuDetail { get { return _readings.CpuDetail; } }
        public string GpuDetail { get { return _readings.GpuDetail; } }
        public string CpuTemperatureDetail { get { return _readings.CpuTemperatureDetail; } }
        public string GpuTemperatureDetail { get { return _readings.GpuTemperatureDetail; } }
        public string FpsText { get { return _readings.FpsText; } }
        public string OverlayFpsText { get { return _readings.OverlayFpsText; } }
        public string FpsDetail { get { return _readings.FpsDetail; } }
        public string FpsTargetText { get { return _readings.FpsTargetText; } }
        public bool IsInstallingDriver { get { return _installer!=null&&_installer.IsInstalling; } }
        public bool CanInstallDriver { get { return !_disposed&&!IsInstallingDriver; } }
        public bool CanCancelDriverPreparation { get { return !_disposed&&_installer!=null&&_installer.CanCancelPreparation; } }
        public string TemperatureStatusText
        {
            get
            {
                string backend=_last==null?"Disabled":_last.SensorBackendStatus;
                string driver=_last==null?"NotChecked":_last.SensorDriverStatus;
                string text=string.Format(Localizer.Culture,Labels["SensorStatus"],SensorState(backend),SensorState(driver));
                if(_installer!=null)text+=" · "+SensorState(_installer.Status);
                return text;
            }
        }
        public string TemperatureStatusDetail
        { get { return CpuTemperatureDetail+Environment.NewLine+GpuTemperatureDetail+(_last==null||string.IsNullOrEmpty(_last.SensorDiagnostic)?string.Empty:Environment.NewLine+_last.SensorDiagnostic); } }
        private string SensorState(string state)
        {
            string actual=state??"NotChecked";
            string key="Sensor"+actual;
            string text=Labels[key];
            if(text!=key)return text;
            return actual.StartsWith("InstallFailed",StringComparison.Ordinal)
                ?Labels["SensorInstallFailed"]+actual.Substring("InstallFailed".Length):actual;
        }
        internal bool BeginDriverInstallation(bool explicitlyConfirmed)
        { return BeginDriverInstallation(explicitlyConfirmed,null); }
        internal bool BeginDriverInstallation(bool explicitlyConfirmed,string localInstallerPath)
        {
            if(_disposed||!explicitlyConfirmed||IsInstallingDriver)return false;
            if(_installer==null)_installer=_createInstaller(OnDriverInstallationFinished);
            bool began=_installer.Begin(true,localInstallerPath);
            if(began)
            {
                if(_installTimer==null)
                {
                    _installTimer=new DispatcherTimer(DispatcherPriority.Background,_dispatcher) { Interval=TimeSpan.FromMilliseconds(250) };
                    _installTimer.Tick+=OnInstallationProgress;
                }
                _installTimer.Start();
            }
            RaiseSensorStatus();return began;
        }
        internal bool CancelDriverPreparation()
        {
            bool cancelled=_installer!=null&&_installer.CancelPreparation();
            if(!_disposed)RaiseSensorStatus();return cancelled;
        }
        private void OnDriverInstallationFinished() { if(!_disposed)_session.RequestDriverReset(); }
        private void OnInstallationProgress(object sender,EventArgs args)
        { if(_disposed||!IsInstallingDriver)_installTimer.Stop();if(!_disposed)RaiseSensorStatus(); }
        private void RaiseSensorStatus()
        {
            // Installer state changes on a worker thread, so its getters remain live.
            // Only the last published values are cached for notification comparisons.
            string text=TemperatureStatusText, detail=TemperatureStatusDetail;
            bool installing=IsInstallingDriver, canInstall=CanInstallDriver, canCancel=CanCancelDriverPreparation;
            string oldText=_sensorStatusText, oldDetail=_sensorStatusDetail;
            bool oldInstalling=_reportedInstalling, oldCanInstall=_reportedCanInstall, oldCanCancel=_reportedCanCancel;
            _sensorStatusText=text; _sensorStatusDetail=detail;
            _reportedInstalling=installing; _reportedCanInstall=canInstall; _reportedCanCancel=canCancel;
            NotifyTextChanged("TemperatureStatusText",oldText,text);
            NotifyTextChanged("TemperatureStatusDetail",oldDetail,detail);
            if(oldInstalling!=installing)RaisePropertyChanged("IsInstallingDriver");
            if(oldCanInstall!=canInstall)RaisePropertyChanged("CanInstallDriver");
            if(oldCanCancel!=canCancel)RaisePropertyChanged("CanCancelDriverPreparation");
        }
        public string GpuName { get { return _readings.GpuName; } }
        public string MemoryText
        {
            get
            {
                var memory=_cleaner.Computer.Memory;
                return memory==null?Labels["Unavailable"]:memory.Physical.Used.Percentage.ToString(CultureInfo.InvariantCulture)+"%";
            }
        }
        public string ActionStatus
        {
            get { return _actionKey==null?_actionStatus:(_actionPrefix??string.Empty)+Labels[_actionKey]+(_actionDetail??string.Empty); }
            set { _actionKey=null;_actionStatus=value??string.Empty;RaisePropertyChanged("ActionStatus"); }
        }
        internal void SetActionStatus(string key,string prefix=null,string detail=null)
        { _actionKey=key;_actionPrefix=prefix;_actionDetail=detail;RaisePropertyChanged("ActionStatus"); }
        private bool Item(int flag) { return (_options.Items&flag)!=0; }
        private void SetItem(int flag,bool value,string name)
        {
            _options.Items=value?_options.Items|flag:_options.Items&~flag;
            _options.Normalize(); SaveOption(name,true);
            RaisePropertyChanged("ShowMemory");
            RaisePropertyChanged("ShowCpuGroup"); RaisePropertyChanged("ShowGpuGroup");
        }
        private void SaveOption(string property,bool demand)
        {
            Settings.Save(); RaisePropertyChanged(property);
            if(demand) UpdateDemand();
        }
        public void SetViewState(bool visible,bool processes)
        {
            if(_disposed||(_mainVisible==visible&&_processPage==processes))return;
            _mainVisible=visible; _processPage=processes;
            _processes.SetActive(visible&&processes);
            if(!visible||!processes)
            {
                if(_last!=null) _last.Processes.Clear();
            }
            UpdateDemand();
        }
        private void ChangeFpsTarget()
        {
            if(_disposed)return;
            Interlocked.Increment(ref _fpsGeneration);_lastFps=null;_fpsFailed=false;
            IFrameRateMonitor monitor=_fps;
            if(monitor!=null)
            {
                try { monitor.SetTarget(FpsTargetMode==0,FpsTargetMode==0?null:CopyFpsTarget(_manualFpsTarget)); }
                catch(Exception) { FailFps(); }
            }
            UpdateDemand();
        }
        private void UpdateFpsDemand()
        {
            // Only the overview or an enabled overlay has a visible FPS reading.
            // Selecting a row, opening settings, hiding or entering compact mode is not capture demand.
            bool requested=ShowFps&&((_mainVisible&&!_processPage)||OverlayEnabled)&&(FpsTargetMode==0||HasFpsTarget);
            if(requested==_fpsActive)return;
            Interlocked.Increment(ref _fpsGeneration);_lastFps=null;_fpsFailed=false;
            if(!requested)
            {
                _fpsActive=false;
                if(_fps!=null) { try { _fps.SetActive(false); } catch(Exception) { _fpsFailed=true; } }
                return;
            }
            try
            {
                if(_fps==null)_fps=_createFps();
                if(_fps==null)throw new InvalidOperationException("The FPS collector factory returned no collector.");
                _fps.SetTarget(FpsTargetMode==0,FpsTargetMode==0?null:CopyFpsTarget(_manualFpsTarget));
                _fps.SetActive(true);_fpsActive=true;
            }
            catch(Exception) { FailFps(); }
        }
        private void FailFps()
        {
            _fpsActive=false;_fpsFailed=true;
            if(_fps!=null) { try { _fps.SetActive(false); } catch(Exception) { } }
        }
        private void UpdateDemand()
        {
            if(_disposed)return;
            UpdateFpsDemand();
            var demand=MonitoringPolicy.GetDemand(_mainVisible,_processPage,OverlayEnabled,_mainVisible||ShowCpuTemperature||ShowGpuTemperature);
            _session.SetDemand(demand);
            if(!demand.Temperatures&&_last!=null)
            {
                _last.CpuTemperature=_last.GpuTemperature=MetricValue.Missing(MetricStatus.Disabled,"Temperature sampling is not requested");
                _last.SensorBackendStatus="Disabled";
            }
            _cleaner.SetMemoryRefreshInterval(demand.CpuGpu?RefreshSeconds:5);
            _pump.SetDemand(demand,RefreshSeconds);
            RefreshReadings();
        }
        public void RefreshNow() { UpdateDemand(); }
        public ListSortDirection Sort(string field) { return _processes.Sort(field); }
        public void RefreshDisplays()
        {
            // Reuse rows by Id: replacing the selected instance clears the ComboBox selection,
            // which writes the primary-display default back over the user's saved display.
            var old=Displays??new List<DisplayChoice>();
            var list=new List<DisplayChoice> { Display(old,string.Empty,Labels["PrimaryDisplay"]) };
            foreach(var screen in Forms.Screen.AllScreens) list.Add(Display(old,screen.DeviceName,screen.DeviceName+" ("+screen.Bounds.Width+"×"+screen.Bounds.Height+")"));
            Displays=list; RaisePropertyChanged("Displays");
        }
        private static DisplayChoice Display(IEnumerable<DisplayChoice> old,string id,string name)
        {
            foreach(var choice in old) if(choice.Id==id) { choice.Name=name; return choice; }
            return new DisplayChoice(id,name);
        }
        public void SavePosition(string screen,double x,double y)
        {
            _options.Screen=screen; _options.Corner=4; _options.X=x; _options.Y=y;
            Settings.Save(); RaisePropertyChanged("OverlayCorner"); RaisePropertyChanged("OverlayScreen");
        }
        public void ReloadOptions()
        {
            _options=Settings.Monitoring; _options.Normalize();
            _manualFpsTarget=null;
            foreach(string name in new[]{"RefreshSeconds","OverlayEnabled","ClickThrough","OverlayLayout","OverlayOrientation","OverlayScreen","OverlayCorner","ShowCpu","ShowGpu","ShowMemory","ShowCpuTemperature","ShowGpuTemperature","ShowCpuGroup","ShowGpuGroup","ShowFps","FpsTargetMode","HasFpsTarget"}) RaisePropertyChanged(name);
            ChangeFpsTarget();
        }
        private void Sample(MonitoringDemand demand,int generation)
        {
            ResourceSnapshot snapshot=null; string failure=null;
            FrameRateReading fps=null;IFrameRateMonitor frameMonitor=_fps;
            int fpsGeneration=Interlocked.CompareExchange(ref _fpsGeneration,0,0);bool fpsActive=_fpsActive;
            try
            {
                if(_disposed)return;
                Interlocked.Increment(ref _sampleCount);
                if(demand.Processes) Interlocked.Increment(ref _processSampleCount);
                if(demand.Temperatures) Interlocked.Increment(ref _temperatureSampleCount);
                snapshot=_session.Sample(demand,RefreshSeconds);
            }
            catch(Exception ex) { failure=Labels["Error"]+": "+ex.Message; }
            if(!_disposed&&fpsActive&&frameMonitor!=null&&fpsGeneration==Interlocked.CompareExchange(ref _fpsGeneration,0,0))
            {
                try { fps=frameMonitor.Sample()??FpsErrorReading(); }
                catch(Exception) { fps=FpsErrorReading(); } // Never expose provider exceptions or stop existing resource sampling.
            }
            // Keep one queued UI callback, but replace its data with the newest
            // current sample. A stale callback must not make a fresh sample vanish.
            lock(_publicationGate)
            {
                if(_disposed||!_pump.IsCurrent(generation))return;
                _publication=new SamplePublication { Snapshot=snapshot,Failure=failure,Fps=fps,Demand=demand,Generation=generation,FpsGeneration=fpsGeneration };
                if(_pending!=0)return;
                _pending=1;
            }
            try { _dispatcher.BeginInvoke(new Action(ApplySample),DispatcherPriority.Background); }
            catch(InvalidOperationException) { lock(_publicationGate) { _publication=null;_pending=0; } }
        }
        private void ApplySample()
        {
            SamplePublication publication;
            lock(_publicationGate) { publication=_publication;_publication=null;_pending=0; }
            if(publication==null||_disposed||!_pump.IsCurrent(publication.Generation))return;
            _last=publication.Snapshot;_failure=publication.Failure;
            if(publication.Demand.Processes)_processes.Apply(publication.Snapshot,publication.Failure);
            if(publication.Snapshot!=null)publication.Snapshot.Processes.Clear();
            if(_fpsActive&&publication.FpsGeneration==Interlocked.CompareExchange(ref _fpsGeneration,0,0)) { _lastFps=publication.Fps;_fpsFailed=false; }
            RefreshReadings();
        }
        private void Suspend() { _session.Suspend(); }
        private void RefreshReadings()
        {
            string fpsText=FrameText();
            var next=new DisplayReadings
            {
                CpuText=Reading(_last==null?null:_last.CpuUsage,false),
                GpuText=Reading(_last==null?null:_last.GpuUsage,false),
                CpuTemperatureText=Reading(_last==null?null:_last.CpuTemperature,true),
                GpuTemperatureText=Reading(_last==null?null:_last.GpuTemperature,true),
                OverlayCpuText=OverlayReading(_last==null?null:_last.CpuUsage,false),
                OverlayGpuText=OverlayReading(_last==null?null:_last.GpuUsage,false),
                OverlayCpuTemperatureText=OverlayReading(_last==null?null:_last.CpuTemperature,true),
                OverlayGpuTemperatureText=OverlayReading(_last==null?null:_last.GpuTemperature,true),
                CpuDetail=Detail(_last==null?null:_last.CpuUsage,false),
                GpuDetail=Detail(_last==null?null:_last.GpuUsage,false),
                CpuTemperatureDetail=Detail(_last==null?null:_last.CpuTemperature,true),
                GpuTemperatureDetail=Detail(_last==null?null:_last.GpuTemperature,false),
                GpuName=_last==null?Labels["WarmingUp"]:_last.GpuName,
                MemoryText=MemoryText,
                FpsText=fpsText,OverlayFpsText=fpsText,FpsDetail=FrameDetail(),FpsTargetText=FrameTargetText()
            };
            var old=_readings;
            _readings=next;
            NotifyTextChanged("CpuText",old.CpuText,next.CpuText);
            NotifyTextChanged("GpuText",old.GpuText,next.GpuText);
            NotifyTextChanged("CpuTemperatureText",old.CpuTemperatureText,next.CpuTemperatureText);
            NotifyTextChanged("GpuTemperatureText",old.GpuTemperatureText,next.GpuTemperatureText);
            NotifyTextChanged("OverlayCpuText",old.OverlayCpuText,next.OverlayCpuText);
            NotifyTextChanged("OverlayGpuText",old.OverlayGpuText,next.OverlayGpuText);
            NotifyTextChanged("OverlayCpuTemperatureText",old.OverlayCpuTemperatureText,next.OverlayCpuTemperatureText);
            NotifyTextChanged("OverlayGpuTemperatureText",old.OverlayGpuTemperatureText,next.OverlayGpuTemperatureText);
            NotifyTextChanged("CpuDetail",old.CpuDetail,next.CpuDetail);
            NotifyTextChanged("GpuDetail",old.GpuDetail,next.GpuDetail);
            NotifyTextChanged("CpuTemperatureDetail",old.CpuTemperatureDetail,next.CpuTemperatureDetail);
            NotifyTextChanged("GpuTemperatureDetail",old.GpuTemperatureDetail,next.GpuTemperatureDetail);
            NotifyTextChanged("GpuName",old.GpuName,next.GpuName);
            NotifyTextChanged("MemoryText",old.MemoryText,next.MemoryText);
            NotifyTextChanged("FpsText",old.FpsText,next.FpsText);
            NotifyTextChanged("OverlayFpsText",old.OverlayFpsText,next.OverlayFpsText);
            NotifyTextChanged("FpsDetail",old.FpsDetail,next.FpsDetail);
            NotifyTextChanged("FpsTargetText",old.FpsTargetText,next.FpsTargetText);
            RaiseSensorStatus();
        }
        private void NotifyTextChanged(string property,string previous,string current)
        {
            if(!string.Equals(previous,current,StringComparison.Ordinal))RaisePropertyChanged(property);
        }
        private static FrameRateReading FpsErrorReading()
        { return new FrameRateReading(MetricValue.Missing(MetricStatus.Error,string.Empty),string.Empty,"FpsError"); }
        private string FrameState()
        {
            if(!ShowFps)return "FpsDisabled";
            if(FpsTargetMode==1&&!HasFpsTarget)return "FpsChooseTarget";
            if(_fpsFailed)return "FpsError";
            if(!_fpsActive)return "FpsDisabled";
            if(_lastFps==null)return "FpsWarmingUp";
            string key=_lastFps.StateKey;
            if(string.IsNullOrEmpty(key)||Labels[key]==key)return "FpsError";
            if(key=="FpsAvailable")
            {
                MetricValue metric=_lastFps.Metric;
                if(metric==null||metric.Status!=MetricStatus.Available||!metric.Value.HasValue||metric.Value.Value<=0
                    ||double.IsNaN(metric.Value.Value)||double.IsInfinity(metric.Value.Value))return "FpsError";
            }
            return key;
        }
        private string FrameText()
        { return FrameState()=="FpsAvailable"?_lastFps.Metric.Value.Value.ToString("F0",CultureInfo.InvariantCulture):"—"; }
        private string FrameDetail()
        {
            string state=FrameState();
            string detail=_fpsActive&&_lastFps!=null&&_lastFps.StateKey==state&&_lastFps.Metric!=null?_lastFps.Metric.Detail:null;
            return Labels[state]+(string.IsNullOrEmpty(detail)?string.Empty:Environment.NewLine+detail);
        }
        private string FrameTargetText()
        {
            if(FpsTargetMode==1)return _manualFpsTarget==null?Labels["FpsChooseTarget"]
                :string.Format(Localizer.Culture,"{0} (PID {1})",_manualFpsTarget.Name,_manualFpsTarget.Id);
            string target=_lastFps==null?null:_lastFps.TargetName;
            return Labels["FpsForeground"]+(string.IsNullOrEmpty(target)?string.Empty:" · "+target);
        }
        private static string OverlayReading(MetricValue metric,bool temperature)
        {
            return metric!=null&&metric.Status==MetricStatus.Available&&metric.Value.HasValue
                ?metric.Value.Value.ToString("F0",CultureInfo.InvariantCulture)+(temperature?"°C":"%")
                :"—";
        }
        private string Reading(MetricValue metric,bool temperature)
        {
            if(metric!=null&&metric.Status==MetricStatus.Available&&metric.Value.HasValue)
                return metric.Value.Value.ToString(temperature?"F0":"F1",CultureInfo.InvariantCulture)+(temperature?" °C":"%");
            return metric!=null&&metric.Status==MetricStatus.WarmingUp?Labels["WarmingUp"]:Labels["Unavailable"];
        }
        private string Detail(MetricValue metric,bool cpuSensor)
        {
            if(!string.IsNullOrEmpty(_failure))return _failure;
            if(metric==null)return Labels["WarmingUp"];
            if(cpuSensor&&metric.Status==MetricStatus.Unsupported)return Labels["NoCpuSensor"]+"："+(metric.Detail??string.Empty);
            string prefix=metric.Status==MetricStatus.Available?string.Empty:Labels[metric.Status.ToString()]+"：";
            return prefix+(metric.Detail??string.Empty);
        }
        private void OnLanguageChanged(object sender,PropertyChangedEventArgs e)
        {
            if(_disposed||e.PropertyName==null||e.PropertyName.Length!=0)return; // Refresh once after the full language switch, not its intermediate Culture event.
            if(!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(new Action(delegate { OnLanguageChanged(sender,e); })); return; }
            Labels.Refresh();
            if(Displays!=null) foreach(var display in Displays) if(string.IsNullOrEmpty(display.Id)) display.Name=Labels["PrimaryDisplay"]; // Rename in place; replacing rows would clear the saved display.
            _processes.RefreshText();
            RaisePropertyChanged("ActionStatus");
            RefreshReadings();
        }
        public void Dispose()
        {
            if(_disposed)return;
            _disposed=true; Localizer.StaticPropertyChanged-=OnLanguageChanged;
            lock(_publicationGate) { _publication=null;_pending=0; }
            _fpsActive=false;Interlocked.Increment(ref _fpsGeneration);_lastFps=null;
            IFrameRateMonitor frames=_fps;_fps=null;
            if(frames!=null) { try { frames.Dispose(); } catch(Exception) { } } // The collector owns asynchronous native cleanup; never join it here.
            if(_installTimer!=null) { _installTimer.Stop();_installTimer.Tick-=OnInstallationProgress; }
            if(_installer!=null)_installer.CancelPreparation();
            _processes.PropertyChanged-=OnProcessPresentationChanged;_processes.Dispose();
            _session.Dispose();_pump.Dispose(); _last=null;
        }
    }
}
#pragma warning restore 1591
