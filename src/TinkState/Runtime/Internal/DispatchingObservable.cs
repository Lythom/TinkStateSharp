using System.Collections.Generic;

namespace TinkState.Internal
{
	interface DispatchingObservable
	{
		string DebugName { get; }
		long GetRevision();
		bool CanFire();
		void Subscribe(Observer observer);
		void Unsubscribe(Observer observer);
	}

	interface DispatchingObservable<T> : DispatchingObservable, ValueProvider<T>
	{
		IEqualityComparer<T> GetComparer();
	}
}