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
    public sealed class DisplayChoice
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public DisplayChoice(string id, string name) { Id=id; Name=name; }
    }

    public sealed class MonitoringViewModel : ObservableObject, IDisposable
    {
        private readonly MainViewModel _cleaner;
        private readonly Dispatcher _dispatcher;
        private readonly MonitoringPump _pump;
        private readonly MonitoringSession _session;
        private readonly Func<Action,SensorDriverInstaller> _createInstaller;
        private SensorDriverInstaller _installer;
        private DispatcherTimer _installTimer;
        private readonly ProcessListPresentation _processes;
        private MonitoringOptions _options;
        private ResourceSnapshot _last;
        private bool _mainVisible, _processPage;
        private volatile bool _disposed;
        private int _pending;
        private int _sampleCount, _processSampleCount, _temperatureSampleCount;
        private string _failure, _actionStatus=string.Empty;

        public MonitoringViewModel(MainViewModel cleaner, Dispatcher dispatcher)
            : this(cleaner,dispatcher,delegate { return new ResourceMonitor(); }) { }
        internal MonitoringViewModel(MainViewModel cleaner,Dispatcher dispatcher,Func<IResourceMonitor> createMonitor)
            : this(cleaner,dispatcher,createMonitor,delegate(Action completed) { return new SensorDriverInstaller(completed); }) { }
        internal MonitoringViewModel(MainViewModel cleaner,Dispatcher dispatcher,Func<IResourceMonitor> createMonitor,Func<Action,SensorDriverInstaller> createInstaller)
        {
            if(cleaner==null)throw new ArgumentNullException("cleaner");
            if(dispatcher==null)throw new ArgumentNullException("dispatcher");
            if(createInstaller==null)throw new ArgumentNullException("createInstaller");
            _cleaner=cleaner; _dispatcher=dispatcher; _options=Settings.Monitoring;
            _createInstaller=createInstaller;_session=new MonitoringSession(createMonitor);
            _options.Normalize(); Labels=new MonitorText();
            _processes=new ProcessListPresentation(dispatcher,Labels);
            _processes.PropertyChanged+=OnProcessPresentationChanged;
            _pump=new MonitoringPump(Sample, Suspend);
            Localizer.StaticPropertyChanged+=OnLanguageChanged;
            RefreshDisplays();
        }
        public MonitorText Labels { get; private set; }
        public ListCollectionView ProcessesView { get { return _processes.View; } }
        public IList<DisplayChoice> Displays { get; private set; }
        public IList<int> RefreshChoices { get { return new List<int>{1,2,5}; } }
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
        public bool ShowCpuGroup { get { return ShowCpu||ShowCpuTemperature; } }
        public bool ShowGpuGroup { get { return ShowGpu||ShowGpuTemperature; } }
        public string SearchQuery { get { return _processes.SearchQuery; } set { _processes.SearchQuery=value; } }
        public ProcessRow SelectedProcess { get { return _processes.SelectedProcess; } set { _processes.SelectedProcess=value; } }
        public int ProcessCount { get { return _processes.TotalCount; } }
        public string ProcessCountText { get { return _processes.CountText; } }
        public bool CanClearSearch { get { return _processes.CanClearSearch; } }
        public bool CanActOnSelectedProcess { get { return _processes.CanActOnSelectedProcess; } }
        public string ProcessStatusText { get { return _processes.StatusText; } }
        public string ProcessStatusDetail { get { return _processes.StatusDetail; } }
        public bool HasProcessStatus { get { return _processes.HasStatus; } }
        public void ClearSearch() { _processes.ClearSearch(); }
        internal bool IsCurrentProcessTarget(ProcessSnapshot target) { return _processes.IsCurrentTarget(target); }
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
        }
        public int SampleCount { get { return _sampleCount; } }
        public int ProcessSampleCount { get { return _processSampleCount; } }
        public int TemperatureSampleCount { get { return _temperatureSampleCount; } }
        public string CpuText { get { return Reading(_last==null?null:_last.CpuUsage,false); } }
        public string GpuText { get { return Reading(_last==null?null:_last.GpuUsage,false); } }
        public string CpuTemperatureText { get { return Reading(_last==null?null:_last.CpuTemperature,true); } }
        public string GpuTemperatureText { get { return Reading(_last==null?null:_last.GpuTemperature,true); } }
        public string OverlayCpuText { get { return OverlayReading(_last==null?null:_last.CpuUsage,false); } }
        public string OverlayGpuText { get { return OverlayReading(_last==null?null:_last.GpuUsage,false); } }
        public string OverlayCpuTemperatureText { get { return OverlayReading(_last==null?null:_last.CpuTemperature,true); } }
        public string OverlayGpuTemperatureText { get { return OverlayReading(_last==null?null:_last.GpuTemperature,true); } }
        public string CpuDetail { get { return Detail(_last==null?null:_last.CpuUsage,false); } }
        public string GpuDetail { get { return Detail(_last==null?null:_last.GpuUsage,false); } }
        public string CpuTemperatureDetail { get { return Detail(_last==null?null:_last.CpuTemperature,true); } }
        public string GpuTemperatureDetail { get { return Detail(_last==null?null:_last.GpuTemperature,false); } }
        public bool IsInstallingDriver { get { return _installer!=null&&_installer.IsInstalling; } }
        public bool CanInstallDriver { get { return !_disposed&&!IsInstallingDriver; } }
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
        {
            if(_disposed||!explicitlyConfirmed||IsInstallingDriver)return false;
            if(_installer==null)_installer=_createInstaller(OnDriverInstallationFinished);
            bool began=_installer.Begin(true);
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
        private void OnDriverInstallationFinished() { if(!_disposed)_session.RequestDriverReset(); }
        private void OnInstallationProgress(object sender,EventArgs args)
        { if(_disposed||!IsInstallingDriver)_installTimer.Stop();if(!_disposed)RaiseSensorStatus(); }
        private void RaiseSensorStatus()
        { foreach(string name in new[]{"TemperatureStatusText","TemperatureStatusDetail","IsInstallingDriver","CanInstallDriver"})RaisePropertyChanged(name); }
        public string GpuName { get { return _last==null?Labels["WarmingUp"]:_last.GpuName; } }
        public string MemoryText
        {
            get
            {
                var memory=_cleaner.Computer.Memory;
                return memory==null?Labels["Unavailable"]:memory.Physical.Used.Percentage.ToString(CultureInfo.InvariantCulture)+"%";
            }
        }
        public string ActionStatus { get { return _actionStatus; } set { _actionStatus=value??string.Empty; RaisePropertyChanged("ActionStatus"); } }
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
        private void UpdateDemand()
        {
            var demand=MonitoringPolicy.GetDemand(_mainVisible,_processPage,OverlayEnabled,_mainVisible||ShowCpuTemperature||ShowGpuTemperature);
            _session.SetDemand(demand);
            if(!demand.Temperatures&&_last!=null)
            {
                _last.CpuTemperature=_last.GpuTemperature=MetricValue.Missing(MetricStatus.Disabled,"Temperature sampling is not requested");
                _last.SensorBackendStatus="Disabled";
                foreach(string name in new[]{"CpuTemperatureText","GpuTemperatureText","OverlayCpuTemperatureText","OverlayGpuTemperatureText","CpuTemperatureDetail","GpuTemperatureDetail"})RaisePropertyChanged(name);
                RaiseSensorStatus();
            }
            _cleaner.SetMemoryRefreshInterval(demand.CpuGpu?RefreshSeconds:5);
            _pump.SetDemand(demand,RefreshSeconds);
        }
        public void RefreshNow() { UpdateDemand(); }
        public ListSortDirection Sort(string field) { return _processes.Sort(field); }
        public void RefreshDisplays()
        {
            var list=new List<DisplayChoice>();
            foreach(var screen in Forms.Screen.AllScreens) list.Add(new DisplayChoice(screen.DeviceName,screen.DeviceName+" ("+screen.Bounds.Width+"×"+screen.Bounds.Height+")"));
            Displays=list; RaisePropertyChanged("Displays");
        }
        public void SavePosition(string screen,double x,double y)
        {
            _options.Screen=screen; _options.Corner=4; _options.X=x; _options.Y=y;
            Settings.Save(); RaisePropertyChanged("OverlayCorner"); RaisePropertyChanged("OverlayScreen");
        }
        public void ReloadOptions()
        {
            _options=Settings.Monitoring; _options.Normalize();
            foreach(string name in new[]{"RefreshSeconds","OverlayEnabled","ClickThrough","OverlayLayout","OverlayOrientation","OverlayScreen","OverlayCorner","ShowCpu","ShowGpu","ShowMemory","ShowCpuTemperature","ShowGpuTemperature","ShowCpuGroup","ShowGpuGroup"}) RaisePropertyChanged(name);
            UpdateDemand();
        }
        private void Sample(MonitoringDemand demand,int generation)
        {
            ResourceSnapshot snapshot=null; string failure=null;
            try
            {
                if(_disposed)return;
                Interlocked.Increment(ref _sampleCount);
                if(demand.Processes) Interlocked.Increment(ref _processSampleCount);
                if(demand.Temperatures) Interlocked.Increment(ref _temperatureSampleCount);
                snapshot=_session.Sample(demand,RefreshSeconds);
            }
            catch(Exception ex) { failure=Labels["Error"]+": "+ex.Message; }
            if(_disposed||!_pump.IsCurrent(generation)||Interlocked.CompareExchange(ref _pending,1,0)!=0)return;
            try
            {
                _dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        if(_disposed||!_pump.IsCurrent(generation))return;
                        _last=snapshot; _failure=failure;
                        if(demand.Processes)_processes.Apply(snapshot,failure);
                        if(snapshot!=null)snapshot.Processes.Clear();
                        foreach(string property in new[]{"CpuText","GpuText","CpuTemperatureText","GpuTemperatureText","OverlayCpuText","OverlayGpuText","OverlayCpuTemperatureText","OverlayGpuTemperatureText","CpuDetail","GpuDetail","CpuTemperatureDetail","GpuTemperatureDetail","GpuName","MemoryText"}) RaisePropertyChanged(property);
                        RaiseSensorStatus();
                    }
                    finally { Interlocked.Exchange(ref _pending,0); }
                }),DispatcherPriority.Background);
            }
            catch(InvalidOperationException) { Interlocked.Exchange(ref _pending,0); }
        }
        private void Suspend() { _session.Suspend(); }
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
            _processes.RefreshText();
            foreach(string name in new[]{"CpuText","GpuText","CpuTemperatureText","GpuTemperatureText","CpuDetail","GpuDetail","CpuTemperatureDetail","GpuTemperatureDetail"})RaisePropertyChanged(name);
            RaiseSensorStatus();
        }
        public void Dispose()
        {
            if(_disposed)return;
            _disposed=true; Localizer.StaticPropertyChanged-=OnLanguageChanged;
            if(_installTimer!=null) { _installTimer.Stop();_installTimer.Tick-=OnInstallationProgress; }
            _processes.PropertyChanged-=OnProcessPresentationChanged;_processes.Dispose();
            _session.Dispose();_pump.Dispose(); _last=null;
        }
    }
}
#pragma warning restore 1591
