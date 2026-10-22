using System;
using System.Reflection;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class ProcessPresentationStateTests
    {
        private static object NewState()
        {
            var type=Assembly.GetExecutingAssembly().GetType("WinMemoryCleaner.ProcessPresentationState");
            Assert.IsNotNull(type,"ProcessPresentationState must distinguish loading, failure and successful empty data");
            return Activator.CreateInstance(type,true);
        }
        private static object Get(object state,string name)
        {
            var property=state.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            Assert.IsNotNull(property,name+" must expose the presentation state");
            return property.GetValue(state,null);
        }
        private static void Call(object state,string name,params object[] arguments)
        {
            var method=state.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            Assert.IsNotNull(method,name+" must implement the approved state transition");
            method.Invoke(state,arguments);
        }
        private static void Status(object state,string expected) { Assert.AreEqual(expected,Get(state,"Status").ToString()); }
        [Test]
        public void InactiveIgnoresSnapshotsFailuresAndFiltering()
        {
            var state=NewState();
            Call(state,"AcceptSnapshot",2,1);Call(state,"RejectSnapshot","fixture failure");Call(state,"SetFiltering",true);
            Status(state,"Inactive");Assert.IsFalse((bool)Get(state,"HasSnapshot"));Assert.IsFalse((bool)Get(state,"HasFailure"));
            Assert.IsFalse((bool)Get(state,"CanUseCurrentData"));Assert.AreEqual(0,Get(state,"TotalCount"));
        }
        [Test]
        public void LoadingAndFailureBeforeFirstSuccessHaveNoSnapshot()
        {
            var state=NewState();Call(state,"SetActive",true);Status(state,"Loading");
            Assert.IsFalse((bool)Get(state,"HasSnapshot"));Assert.IsFalse((bool)Get(state,"CanUseCurrentData"));
            Call(state,"RejectSnapshot","fixture failure");Status(state,"Failed");
            Assert.AreEqual("fixture failure",Get(state,"FailureDetail"));Assert.IsFalse((bool)Get(state,"HasSnapshot"));
            Call(state,"SetFiltering",true);Status(state,"Failed");
        }
        [TestCase(0,0,"Empty")]
        [TestCase(2,0,"NoMatches")]
        [TestCase(2,1,"Ready")]
        public void SuccessfulSnapshotsDistinguishEmptyNoMatchesAndReady(int total,int matched,string expected)
        {
            var state=NewState();Call(state,"SetActive",true);Call(state,"AcceptSnapshot",total,matched);
            Status(state,expected);Assert.IsTrue((bool)Get(state,"HasSnapshot"));Assert.IsTrue((bool)Get(state,"CanUseCurrentData"));
            Assert.AreEqual(total,Get(state,"TotalCount"));Assert.AreEqual(matched,Get(state,"MatchCount"));
        }
        [Test]
        public void StaleDataKeepsCountsButLocalFilteringCannotClearFailure()
        {
            var state=NewState();Call(state,"SetActive",true);Call(state,"AcceptSnapshot",2,1);
            Call(state,"RejectSnapshot","fixture failure");Call(state,"SetFiltering",true);Call(state,"UpdateCounts",2,0);
            Status(state,"Stale");Assert.AreEqual(2,Get(state,"TotalCount"));Assert.AreEqual(0,Get(state,"MatchCount"));
            Assert.IsFalse((bool)Get(state,"CanUseCurrentData"));Assert.IsTrue((bool)Get(state,"HasFailure"));
            Call(state,"SetFiltering",false);Status(state,"Stale");Assert.AreEqual("fixture failure",Get(state,"FailureDetail"));
        }
        [Test]
        public void RecoveryClearsFailureButPendingQueryStillPausesActions()
        {
            var state=NewState();Call(state,"SetActive",true);Call(state,"RejectSnapshot","fixture failure");
            Call(state,"SetFiltering",true);Call(state,"AcceptSnapshot",2,1);Status(state,"Filtering");
            Assert.IsFalse((bool)Get(state,"HasFailure"));Assert.AreEqual(string.Empty,Get(state,"FailureDetail"));
            Assert.IsFalse((bool)Get(state,"CanUseCurrentData"));Call(state,"SetFiltering",false);Status(state,"Ready");
            Assert.IsTrue((bool)Get(state,"CanUseCurrentData"));
        }
        [Test]
        public void LocalCountsDoNotInventFirstSuccessAndLeavingResetsHistory()
        {
            var state=NewState();Call(state,"SetActive",true);Call(state,"UpdateCounts",2,1);Status(state,"Loading");
            Assert.IsFalse((bool)Get(state,"HasSnapshot"));
            Call(state,"AcceptSnapshot",2,1);Call(state,"RejectSnapshot","fixture failure");Call(state,"SetActive",false);
            Status(state,"Inactive");Assert.AreEqual(0,Get(state,"TotalCount"));Assert.IsFalse((bool)Get(state,"HasFailure"));
            Call(state,"SetActive",true);Status(state,"Loading");Assert.IsFalse((bool)Get(state,"HasSnapshot"));
        }
    }
}
#pragma warning restore 1591
