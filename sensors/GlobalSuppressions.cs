using System.Diagnostics.CodeAnalysis;

// Analyzer suppressions for the isolated sensor backend, mirroring src/Core/GlobalSuppressions.cs.
// Every entry here is a deliberate, reviewed exception, not a relaxation of the canonical ruleset.
[assembly: SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Sampling, message loops and device reads must degrade to a reported status or exit code instead of crashing; unexpected hardware/library failures are exactly what must not escape.")]
[assembly: SuppressMessage("Reliability", "CA9998:Migrate from FxCop analyzers to .NET analyzers", Justification = "Legacy .NET Framework version")]
[assembly: SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Shared protocol type; the host application instantiates it, this backend only links the same source.", Scope = "type", Target = "~T:WinMemoryCleaner.Sensors.SensorFreshness")]
[assembly: SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The standard input/output streams are borrowed from the parent process and are closed by Program after Run returns; the host never owns them.", Scope = "type", Target = "~T:WinMemoryCleaner.Sensors.SensorHost")]
[assembly: SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Reset() releases the native certificate handle on every supported framework version, while Dispose does not exist on .NET 4.0, which the host application also targets.", Scope = "type", Target = "~T:WinMemoryCleaner.Sensors.SensorDriverTrust")]
