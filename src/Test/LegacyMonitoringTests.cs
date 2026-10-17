using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class LegacyMonitoringTests
    {
        private static MainViewModel Create()
        {
            Assert.IsTrue((bool)typeof(App).GetProperty("IsTestBuild", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null, null), "This fixture must use the isolated COMMUNITY_TESTS build.");
            Settings.UseHotkey = false;
            return new MainViewModel(new ComputerService(), new HotkeyService(), new NotificationService(null));
        }

        [Test]
        public void ExclusionCandidates_GetterDoesNotEnumerateOrReplaceTheSnapshot()
        {
            using (var vm = Create())
            {
                var first = vm.Processes;
                Assert.AreEqual(0, first.Count, "Candidates are loaded only when the picker is opened.");
                Assert.AreSame(first, vm.Processes, "Reading a binding must not rescan processes.");
            }
        }

        [Test]
        public void ExclusionCandidates_ExplicitRefreshProducesStableSortedNames()
        {
            using (var vm = Create())
            {
                var refresh = typeof(MainViewModel).GetMethod("RefreshProcesses");
                Assert.IsNotNull(refresh, "Explicit refresh is required instead of getter enumeration.");
                refresh.Invoke(vm, null);
                var snapshot = vm.Processes;
                Assert.Greater(snapshot.Count, 0);
                Assert.AreEqual(snapshot.Count, snapshot.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                CollectionAssert.AreEqual(snapshot.OrderBy(name => name).ToArray(), snapshot.ToArray());
                Assert.AreSame(snapshot, vm.Processes);
            }
        }

        [Test]
        public void MemoryRefreshCadence_DefaultsToHiddenFiveSecondsAndAcceptsOnlySupportedChoices()
        {
            using (var vm = Create())
            {
                var setter = typeof(MainViewModel).GetMethod("SetMemoryRefreshInterval");
                var getter = typeof(MainViewModel).GetProperty("MemoryRefreshSeconds");
                Assert.IsNotNull(setter, "The shared memory source must follow consumer cadence.");
                Assert.IsNotNull(getter);
                Assert.AreEqual(5, getter.GetValue(vm, null));
                foreach (int interval in new[] { 1, 2, 5 })
                {
                    setter.Invoke(vm, new object[] { interval });
                    Assert.AreEqual(interval, getter.GetValue(vm, null));
                }
                setter.Invoke(vm, new object[] { 3 });
                Assert.AreEqual(2, getter.GetValue(vm, null));
            }
        }

        [Test]
        public void MemoryReplacement_NotifiesOnlyMemoryInsteadOfInvalidatingTheComputerRoot()
        {
            var computer = new Computer();
            var observable = computer as INotifyPropertyChanged;
            Assert.IsNotNull(observable, "Nested memory changes must notify without invalidating every Computer binding.");
            string name = null;
            int notifications = 0;
            observable.PropertyChanged += delegate(object sender, PropertyChangedEventArgs e) { name = e.PropertyName; notifications++; };
            computer.Memory = new Memory();
            Assert.AreEqual("Memory", name);
            Assert.AreEqual(1, notifications);
        }
    }
}
#pragma warning restore 1591
