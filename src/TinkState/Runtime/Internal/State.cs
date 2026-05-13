using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TinkState.Internal
{
	[System.Diagnostics.DebuggerDisplay("State {DebugName,nq} = {value}")]
	class State<T> : Dispatcher, TinkState.State<T>, DispatchingObservable<T>
	{
		readonly IEqualityComparer<T> comparer;
		T value;
		public string Name;
		internal string callerFile;
		internal int callerLine;

		public string DebugName
		{
			get
			{
				if (Name != null) return Name;
				if (callerFile == null) return "State<" + typeof(T).Name + ">";
				// alloc uniquement à la lecture (catch / watcher) — pas en hot path
				return System.IO.Path.GetFileNameWithoutExtension(callerFile) + ":" + callerLine;
			}
		}

		public override string ToString() => "State<" + typeof(T).Name + "> " + DebugName + " = " + value;

		public State(T value, IEqualityComparer<T> comparer)
		{
			this.value = value;
			this.comparer = comparer ?? EqualityComparer<T>.Default;
		}

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		public T Value
		{
			get => AutoObservable.Track(this);
			set
			{
				// TODO: warn if called from inside bindings, add a way to run atomically
				if (!comparer.Equals(value, this.value))
				{
					this.value = value;
					Fire();
				}
			}
		}

		public IDisposable Bind(Action<T> callback, IEqualityComparer<T> comparer = null, Scheduler scheduler = null)
		{
			return new Binding<T>(this, callback, comparer, scheduler);
		}

		public Observable<TOut> Map<TOut>(Func<T, TOut> transform, IEqualityComparer<TOut> comparer = null)
		{
			return new TransformObservable<T, TOut>(this, transform, comparer);
		}

		public T GetCurrentValue()
		{
			return value;
		}

		IEqualityComparer<T> DispatchingObservable<T>.GetComparer()
		{
			return comparer;
		}

		long DispatchingObservable.GetRevision()
		{
			return revision;
		}
	}
}