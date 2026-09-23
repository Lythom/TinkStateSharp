using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using TinkState.Internal;

// this is an intentionally undocumented/temporary way for third-party assemblies to access our internals,
// mostly provided so I can experiment with UI Toolkit integration without changing the library for now
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("TinkState.InternalAccess")]

namespace TinkState
{
	/// <summary>
	/// Piece of observable data holding value of type <typeparamref name="T"/> with means to read the value and bind to its updates.
	/// </summary>
	/// <typeparam name="T">Type of the value being managed by this observable.</typeparam>
	public interface Observable<out T>
	{
		/// <summary>
		/// Current value of this observable object.
		/// </summary>
		/// <remarks>
		/// Depending on implementation, accessing this property might cause computation, so it's advised to only access it once
		/// and cache the value in a local variable if it is to be used multiple times.
		/// </remarks>
		/// <value>Current value of this observable object.</value>
		T Value { get; }

		/// <summary>
		/// Subscribe to value changes and attach given <paramref name="callback"/> to be invoked when the value is updated.
		/// </summary>
		/// <remarks>
		///     <para>
		///     The <paramref name="callback"/> will be immediately invoked once when binding.
		///     </para>
		///     <para>
		///     Normally the <paramref name="callback"/> is not invoked directly on value update. Instead bindings are batched
		///     together and scheduled to run once per frame. It is however possible to control this by passing a custom <paramref name="scheduler"/> object.
		///     </para>
		/// </remarks>
		/// <param name="callback">A function to invoke with the updated value.</param>
		/// <param name="comparer">Custom comparer that will be used to determine if the value has changed for this binding to be triggered.</param>
		/// <param name="scheduler">Custom scheduler that will manage invoking the callback.</param>
		/// <returns>Disposable reference to the binding object for later unbinding.</returns>
		IDisposable Bind(Action<T> callback, IEqualityComparer<T> comparer = null, Scheduler scheduler = null);

		/// <summary>
		/// Create a new observable from this observable that maps the value using given <paramref name="transform"/> function.
		/// </summary>
		/// <remarks>
		/// 	<para>
		/// 	The same effect can be achieved with the <see cref="Observable.Auto{T}(System.Func{T},System.Collections.Generic.IEqualityComparer{T})"/> constructor,
		/// 	however this method is more efficient as it doesn't require managing multiple subscriptions internally.
		/// 	</para>
		///		<para>NOTE: this also means that observable access are not tracked within <paramref name="transform"/> function.</para>
		/// </remarks>
		/// <param name="transform">Function to transform values of this observable to values of a new one.</param>
		/// <param name="comparer">Custom comparer for the new observable to determine if bindings and derived auto-observables should be triggered.</param>
		/// <typeparam name="TOut">Type of the values of the resulting observable.</typeparam>
		/// <returns>The new observable with values mapped from this one.</returns>
		Observable<TOut> Map<TOut>(Func<T, TOut> transform, IEqualityComparer<TOut> comparer = null);

		// TODO: extension method to easily map async observables

		// TODO: maybe provide some equivalent of `ObservableTools.bindWithInitial` from Forge to distinguish first binding
		// from subsequent ones, because often we want initial value to be set right away, while changes should be animated
		// could also be an extension method
	}

	/// <summary>
	/// Static class containing <c>Observable</c> constructor methods and references to global utilities.
	/// </summary>
	public static class Observable
	{
		/// <summary>
		/// Quand vrai, AutoObservable.GetCurrentValue collecte toutes les subscriptions
		/// changées dans LastTriggerSources (perd l'early-break, alloue un buffer la
		/// première fois). Activé en éditeur uniquement par défaut.
		/// </summary>
		public static bool DebugTrackTriggers =
#if UNITY_EDITOR
			true;
#else
			false;
#endif
		/// <summary>
		/// Create a lightweight constant observable holding a value that never changes.
		/// </summary>
		/// <typeparam name="T">Type of the value being hold by this observable.</typeparam>
		/// <param name="value">Value for the observable to hold.</param>
		/// <returns>Constant observable object holding given <paramref name="value"/>.</returns>
		public static Observable<T> Const<T>(T value)
		{
			return new ConstObservable<T>(value);
		}

		/// <summary>
		///     <para>
		///     Create an observable containing data computed using given <paramref name="compute"/> function.
		///     </para>
		///     <para>
		///     Any access to <see cref="Observable{T}.Value">Observable.Value</see> from within <paramref name="compute"/> calls
		///     will be automatically tracked by this observable to recompute its value and trigger its bindings.
		///     </para>
		/// </summary>
		/// <typeparam name="T">Type of the value being managed by this observable.</typeparam>
		/// <param name="compute">Computation function that returns a new value for this observable.</param>
		/// <param name="comparer">
		///     <para>Custom comparer that will be used to determine if the value has changed.</para>
		///     <para>Used for triggering bindings and propagating changes to further auto-observables.</para>
		/// </param>
		/// <returns>Auto-observable providing computed values.</returns>
		public static Observable<T> Auto<T>(
			Func<T> compute,
			IEqualityComparer<T> comparer = null,
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			var o = new AutoObservable<T>(new SyncComputation<T>(compute), comparer);
			o.callerFile = callerFile;
			o.callerLine = callerLine;
			return o;
		}

		/// <summary>
		/// Same as <see cref="Auto{T}(System.Func{T},System.Collections.Generic.IEqualityComparer{T})"/> but with an
		/// optional human-readable <paramref name="name"/>, used by debugger displays and
		/// <see cref="AutoObservable{T}.LastTriggerSource"/> output.
		/// </summary>
		public static Observable<T> Auto<T>(
			string name,
			Func<T> compute,
			IEqualityComparer<T> comparer = null,
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			var o = new AutoObservable<T>(new SyncComputation<T>(compute), comparer);
			o.Name = name;
			o.callerFile = callerFile;
			o.callerLine = callerLine;
			return o;
		}

		/// <summary>
		///     <para>
		///     Create an observable containing data computed using given <paramref name="compute"/> function.
		///     </para>
		///		<para>
		///		The difference with the <see cref="Auto{T}(System.Func{T},System.Collections.Generic.IEqualityComparer{T})"/> constructor
		///		is that this one takes an <c>async</c> function that does the computation asynchronously and the actual observable
		///     value will be of type <see cref="AsyncComputeResult{T}"/> representing the current status of asynchronous computation.
		///		</para>
		///     <para>
		///     Any access to <see cref="Observable{T}.Value">Observable.Value</see> from within <paramref name="compute"/> calls
		///     will be automatically tracked by this observable to recompute its value and trigger its bindings.
		///     </para>
		/// </summary>
		/// <param name="compute">Computation function that asynchronously returns a new value for this observable.</param>
		/// <typeparam name="T">Type of the value being managed by this observable.</typeparam>
		/// <returns>Auto-observable providing current results of value computation.</returns>
		public static Observable<AsyncComputeResult<T>> Auto<T>(
			Func<AsyncComputeTask<T>> compute,
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			var computation = new AsyncComputation<T>(compute);
			var observable = new AutoObservable<AsyncComputeResult<T>>(computation, null);
			observable.callerFile = callerFile;
			observable.callerLine = callerLine;
			computation.Init(observable);
			return observable;
		}

		/// <summary>
		/// Create an observable containing data computed using given <paramref name="compute"/> function,
		/// similarly to <see cref="Auto{T}(System.Func{AsyncComputeTask{T}})"/> but with support for cancelling
		/// computations.
		/// </summary>
		/// <remarks>
		/// The <paramref name="compute"/> function receives a <see cref="CancellationToken"/> instance that will be automatically
		/// canceled if the auto-observable triggers a new computation (e.g. if one of its dependencies changes and we need to calculate a new value).
		/// </remarks>
		/// <param name="compute">Cancelable computation function that asynchronously returns a new value for this observable.</param>
		/// <typeparam name="T">Type of the value being managed by this observable.</typeparam>
		/// <returns>Auto-observable providing current results of value computation.</returns>
		public static Observable<AsyncComputeResult<T>> Auto<T>(
			Func<CancellationToken, AsyncComputeTask<T>> compute,
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			// TODO: some flag for also cancelling task when last binding is disposed?
			var computation = new AsyncCancelableComputation<T>(compute);
			var observable = new AutoObservable<AsyncComputeResult<T>>(computation, null);
			observable.callerFile = callerFile;
			observable.callerLine = callerLine;
			computation.Init(observable);
			return observable;
		}

		/// <summary>
		/// Invoke given <paramref name="action"/> and track Observable access inside it, similarly to <see cref="Auto{T}(System.Func{T},System.Collections.Generic.IEqualityComparer{T})"/>
		/// When any of the the tracked values changes, the action is scheduled for invocation again,
		/// in the same way as <see cref="Observable{T}.Bind"/> callbacks are.
		/// </summary>
		/// <param name="action">Action to be invoked immediately as well as when any of the tracked value changes.</param>
		/// <param name="scheduler">Custom scheduler that will manage invoking the action.</param>
		/// <returns>Disposable reference to the binding for cancelling further re-invocations.</returns>
		public static IDisposable AutoRun(
			Action action,
			Scheduler scheduler = null,
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			return AutoRun(null, action, scheduler, callerFile, callerLine);
		}

		/// <summary>
		/// Same as <see cref="AutoRun(System.Action,TinkState.Scheduler)"/> but with an optional human-readable
		/// <paramref name="name"/>, useful for debugging — appears in the underlying auto-observable's
		/// <c>[DebuggerDisplay]</c> and helps identify the run inside <c>AutoObservable.Current</c> while stepping.
		/// </summary>
		public static IDisposable AutoRun(
			string name,
			Action action,
			Scheduler scheduler = null,
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			// TODO: support for cancellation from within action? async auto-runs?
			var observable = new AutoObservable<long>(new AutoRunComputation(action), null);
			observable.Name = name;
			observable.callerFile = callerFile;
			observable.callerLine = callerLine;
			return observable.Bind(_ => { }, null, scheduler);
		}

		/// <summary>
		/// Create a mutable observable containing given <paramref name="initialValue"/>.
		/// </summary>
		/// <param name="initialValue">Initial value for this <c>State</c> to hold.</param>
		/// <param name="comparer">
		///     <para>Custom comparer that will be used to determine if the value has changed.</para>
		///     <para>Used for triggering bindings and propagating changes to auto-observables.</para>
		/// </param>
		public static State<T> State<T>(
			T initialValue,
			IEqualityComparer<T> comparer = null,
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			var s = new Internal.State<T>(initialValue, comparer);
			s.callerFile = callerFile;
			s.callerLine = callerLine;
			return s;
		}

		/// <summary>
		/// Same as <see cref="State{T}(T,System.Collections.Generic.IEqualityComparer{T})"/> but with an optional
		/// human-readable <paramref name="name"/>, used by debugger displays so the state can be identified
		/// when it appears as <c>LastTriggerSource</c> of an auto-observable.
		/// </summary>
		public static State<T> State<T>(T initialValue, string name, IEqualityComparer<T> comparer = null)
		{
			return new Internal.State<T>(initialValue, comparer) { Name = name };
		}

		/// <summary>
		/// Debug helper: the auto-observable currently being computed, or <c>null</c> if no compute is in progress.
		/// </summary>
		/// <remarks>
		/// While stepping into the body of an <c>Observable.Auto</c> / <c>Observable.AutoRun</c>, evaluating this
		/// from a debugger watch window gives access to <c>LastTriggerSource</c> — the source observable
		/// whose change caused the current recompute.
		/// </remarks>
		public static object CurrentAuto => Internal.AutoObservable.Current;

		/// <summary>
		/// Create an empty observable list.
		/// </summary>
		/// <typeparam name="T">The type of elements in the list.</typeparam>
		/// <returns>New observable list instance.</returns>
		public static ObservableList<T> List<T>(
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			var l = new Internal.ObservableList<T>();
			l.callerFile = callerFile;
			l.callerLine = callerLine;
			return l;
		}

		/// <summary>
		/// Create an observable list initialized with items from given <paramref name="initial"/> collection.
		/// </summary>
		/// <typeparam name="T">The type of elements in the list.</typeparam>
		/// <returns>New observable list instance.</returns>
		public static ObservableList<T> List<T>(
			IEnumerable<T> initial,
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			var l = new Internal.ObservableList<T>(initial);
			l.callerFile = callerFile;
			l.callerLine = callerLine;
			return l;
		}

		/// <summary>
		/// Create an empty observable dictionary.
		/// </summary>
		/// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
		/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
		/// <returns>New observable dictionary instance.</returns>
		public static ObservableDictionary<TKey, TValue> Dictionary<TKey, TValue>(
			[CallerFilePath] string callerFile = null,
			[CallerLineNumber] int callerLine = 0)
		{
			var d = new Internal.ObservableDictionary<TKey, TValue>();
			d.callerFile = callerFile;
			d.callerLine = callerLine;
			return d;
		}

		// TODO: Dictionary with initial value

		/// <summary>
		/// Default scheduler used for bindings.
		/// </summary>
		/// <remarks>
		/// This should be set to an environment-specific implementation before doing any bindings.
		/// </remarks>
		public static Scheduler Scheduler = Scheduler.Direct;
	}
}