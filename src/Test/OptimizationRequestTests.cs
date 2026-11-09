using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;

#pragma warning disable 1591
namespace WinMemoryCleaner.Test
{
    [TestFixture]
    public sealed class OptimizationRequestTests
    {
        private static object Gate()
        {
            var type=typeof(MainViewModel).Assembly.GetType("WinMemoryCleaner.OptimizationRequestGate");
            Assert.IsNotNull(type,"Optimization requests need admission before ThreadPool dispatch, not only inside the worker.");
            return Activator.CreateInstance(type,true);
        }
        private static bool Enter(object gate)
        { return (bool)gate.GetType().GetMethod("TryEnter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gate,null); }
        private static void Exit(object gate)
        { gate.GetType().GetMethod("Exit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gate,null); }

        [Test]
        public void PendingRequestRejectsReentryAndCompletionAllowsTheNextRequest()
        {
            object gate=Gate();Assert.IsTrue(Enter(gate));Assert.IsFalse(Enter(gate));
            Exit(gate);Assert.IsTrue(Enter(gate));Exit(gate);
        }

        [Test]
        public void ConcurrentRequestsAdmitExactlyOneWorker()
        {
            object gate=Gate();int admitted=0,remaining=16;
            using(var start=new ManualResetEvent(false))
            using(var done=new ManualResetEvent(false))
            {
                for(int i=0;i<16;i++)ThreadPool.QueueUserWorkItem(delegate
                {
                    start.WaitOne();if(Enter(gate))Interlocked.Increment(ref admitted);
                    if(Interlocked.Decrement(ref remaining)==0)done.Set();
                });
                start.Set();Assert.IsTrue(done.WaitOne(5000));Assert.AreEqual(1,admitted);
            }
            Exit(gate);Assert.IsTrue(Enter(gate));Exit(gate);
        }
    }
}
#pragma warning restore 1591
