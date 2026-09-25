using System.Runtime.CompilerServices;
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace TinkState.Internal
{
	interface AsyncComputeTaskSource<T>
	{
		AsyncComputeResult<T> GetResult();
		void OnComplete(Action callback);
		void CancelOnComplete();
	}

	interface AsyncComputeRunner<T>
	{
		Action MoveNext { get; }
		AsyncComputeTask<T> Task { get; }

		void SetResult(T result);
		void SetException(Exception exception);
	}

	class AsyncComputeRunner<TStateMachine, T> : AsyncComputeRunner<T>, AsyncComputeTaskSource<T> where TStateMachine : IAsyncStateMachine
	{
		public AsyncComputeTask<T> Task => new AsyncComputeTask<T>(this);
		public Action MoveNext { get; }

		// Mutable: in release builds the state machine is a struct that advances in place.
		TStateMachine stateMachine;
		readonly Derived owner;
		Action callback;
		AsyncComputeResult<T> result;

		public AsyncComputeRunner(Derived owner)
		{
			MoveNext = DoMoveNext; // allocate closure right away to prevent cache checks later
			this.owner = owner;
		}

		public void Capture(ref TStateMachine stateMachine)
		{
			this.stateMachine = stateMachine;
		}

		void DoMoveNext()
		{
			AutoObservable.ComputeFor(owner, ref stateMachine);
		}

		public void SetResult(T result)
		{
			this.result = AsyncComputeResult<T>.CreateDone(result);
			if (callback != null) callback();
		}

		public void SetException(Exception exception)
		{
			this.result = AsyncComputeResult<T>.CreateFailed(exception);
			if (callback != null) callback();
		}

		public AsyncComputeResult<T> GetResult()
		{
			return result;
		}

		public void OnComplete(Action callback)
		{
			if (result.Status == AsyncComputeStatus.Loading)
			{
				this.callback = callback;
			}
			else
			{
				// this should never be called as we check for status in AsyncComputation
				callback();
			}
		}

		public void CancelOnComplete()
		{
			callback = null;
		}
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	public struct AsyncComputeTaskBuilder<T>
	{
		AsyncComputeRunner<T> runner;
		T result;
		Exception exception;

		public static AsyncComputeTaskBuilder<T> Create()
		{
			return default;
		}

		public AsyncComputeTask<T> Task
		{
			get
			{
				if (runner != null)
				{
					return runner.Task;
				}
				else if (exception != null)
				{
					return AsyncComputeTask<T>.FromException(exception);
				}
				else
				{
					return AsyncComputeTask<T>.FromResult(result);
				}
			}
		}

		public void Start<TStateMachine>(ref TStateMachine stateMachine)
			where TStateMachine : IAsyncStateMachine
		{
			stateMachine.MoveNext();
		}

		public void SetResult(T result)
		{
			if (runner != null)
			{
				runner.SetResult(result);
			}
			else
			{
				this.result = result;
			}
		}

		public void SetException(Exception exception)
		{
			if (runner != null)
			{
				runner.SetException(exception);
			}
			else
			{
				this.exception = exception;
			}
		}

		[ExcludeFromCodeCoverage] // no idea when this is called
		public void SetStateMachine(IAsyncStateMachine stateMachine)
		{
			// when this is even called...
		}

		[ExcludeFromCodeCoverage] // no idea when this is called
		public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : INotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			awaiter.OnCompleted(GetRunner(ref stateMachine).MoveNext);
		}

		public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
			where TAwaiter : ICriticalNotifyCompletion
			where TStateMachine : IAsyncStateMachine
		{
			awaiter.UnsafeOnCompleted(GetRunner(ref stateMachine).MoveNext);
		}

		AsyncComputeRunner<T> GetRunner<TStateMachine>(ref TStateMachine stateMachine)
			where TStateMachine : IAsyncStateMachine
		{
			if (runner != null) return runner;

			// This builder lives inside the state machine. In release builds the state machine is a struct,
			// so the runner is assigned before the copy: the copy that keeps running must know its runner,
			// or its result never reaches the observable.
			var newRunner = new AsyncComputeRunner<TStateMachine, T>(AutoObservable.Current);
			runner = newRunner;
			newRunner.Capture(ref stateMachine);
			return newRunner;
		}
	}


}