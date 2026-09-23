using System;
using NUnit.Framework;
using TinkState;

namespace Test
{
	class TestAllocations : BaseTest
	{
		const int Cycles = 1000;

		static long BytesPerCycle(Action cycle)
		{
			for (var i = 0; i < 100; i++) cycle();
			var before = GC.GetAllocatedBytesForCurrentThread();
			for (var i = 0; i < Cycles; i++) cycle();
			return (GC.GetAllocatedBytesForCurrentThread() - before) / Cycles;
		}

		[Test]
		public void AutoRunWithOneDependency_CreatedThenDisposed()
		{
			var a = Observable.State(1);
			Action read = () => _ = a.Value;

			var bytes = BytesPerCycle(() => Observable.AutoRun(read).Dispose());

			TestContext.Progress.WriteLine($"AutoRun, 1 dependency: {bytes} bytes per cycle");
		}

		[Test]
		public void AutoRunWithThreeDependencies_CreatedThenDisposed()
		{
			var a = Observable.State(1);
			var b = Observable.State("b");
			var c = Observable.State(true);
			Action read = () => _ = a.Value + b.Value + c.Value;

			var bytes = BytesPerCycle(() => Observable.AutoRun(read).Dispose());

			TestContext.Progress.WriteLine($"AutoRun, 3 dependencies: {bytes} bytes per cycle");
		}

		[Test]
		public void AutoRunWithThreeDependencies_Rerun()
		{
			var a = Observable.State(1);
			var b = Observable.State("b");
			var c = Observable.State(true);
			Action read = () => _ = a.Value + b.Value.Length + (c.Value ? 1 : 0);
			var run = Observable.AutoRun(read);

			var bytes = BytesPerCycle(() => a.Value++);
			run.Dispose();

			TestContext.Progress.WriteLine($"AutoRun, 3 dependencies, rerun: {bytes} bytes per cycle");
		}
	}
}
