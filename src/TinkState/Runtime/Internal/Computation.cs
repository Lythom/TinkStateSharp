using System;

namespace TinkState.Internal
{
	interface Computation<out TResult>
	{
		TResult GetNext();
		bool IsPending();
		void Wakeup();
		void Sleep();
	}

	class SyncComputation<T> : Computation<T>
	{
		readonly Func<T> compute;

		public SyncComputation(Func<T> compute)
		{
			this.compute = compute;
		}

		public T GetNext()
		{
			return compute();
		}

		public bool IsPending()
		{
			return false;
		}

		public void Sleep()
		{
		}

		public void Wakeup()
		{
		}
	}

	// The computation behind AutoRun: each run yields a new count, so every run triggers the binding.
	// One object in place of a closure, its delegate and a SyncComputation.
	sealed class AutoRunComputation : Computation<long>
	{
		readonly Action action;
		long runs;

		public AutoRunComputation(Action action)
		{
			this.action = action;
		}

		public long GetNext()
		{
			runs++;
			action();
			return runs;
		}

		public bool IsPending()
		{
			return false;
		}

		public void Sleep()
		{
		}

		public void Wakeup()
		{
		}
	}
}