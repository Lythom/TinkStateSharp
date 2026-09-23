using System;
using NUnit.Framework;
using TinkState;

namespace Test
{
	class TestExceptions : BaseTest
	{
		[Test]
		public void ThrowingComputation_RestoresTrackingContext()
		{
			var source = Observable.State(0);
			var auto = Observable.Auto(() =>
			{
				if (source.Value == 1) throw new InvalidOperationException();
				return source.Value;
			});
			var binding = auto.Bind(_ => { });

			Assert.Throws<InvalidOperationException>(() => source.Value = 1);

			Assert.That(Observable.CurrentAuto, Is.Null);
			binding.Dispose();
		}

		[Test]
		public void ThrowingObserver_LeavesLaterSubscriptionsImmediate()
		{
			var source = Observable.State(0);
			var throwing = source.Bind(value =>
			{
				if (value == 1) throw new InvalidOperationException();
			});

			Assert.Throws<InvalidOperationException>(() => source.Value = 1);

			var calls = 0;
			var later = source.Bind(_ => calls++);
			source.Value = 2;

			Assert.That(calls, Is.EqualTo(2));
			throwing.Dispose();
			later.Dispose();
		}
	}
}
