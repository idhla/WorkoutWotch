﻿namespace WorkoutWotch.UnitTests.Models.Actions
{
    using System;
    using System.Reactive.Linq;
    using System.Reactive.Threading.Tasks;
    using System.Threading;
    using System.Threading.Tasks;
    using Kent.Boogaart.PCLMock;
    using NUnit.Framework;
    using ReactiveUI;
    using WorkoutWotch.Models;
    using WorkoutWotch.Models.Actions;
    using WorkoutWotch.UnitTests.Services.Delay.Mocks;

    [TestFixture]
    public class WaitActionFixture
    {
        [Test]
        public void ctor_throws_if_delay_service_is_null()
        {
            Assert.Throws<ArgumentNullException>(() => new WaitAction(null, TimeSpan.Zero));
        }

        [Test]
        public void ctor_throws_if_delay_is_less_than_zero()
        {
            Assert.Throws<ArgumentException>(() => new WaitAction(new DelayServiceMock(), TimeSpan.FromSeconds(-1)));
        }

        [TestCase(0)]
        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(23498)]
        public void duration_yields_the_duration_passed_into_ctor(int delayInMs)
        {
            var sut = new WaitActionBuilder()
                .WithDelay(TimeSpan.FromMilliseconds(delayInMs))
                .Build();

            Assert.AreEqual(TimeSpan.FromMilliseconds(delayInMs), sut.Duration);
        }

        [Test]
        public void execute_async_throws_if_context_is_null()
        {
            var sut = new WaitActionBuilder().Build();

            Assert.Throws<ArgumentNullException>(async () => await sut.ExecuteAsync(null));
        }

        [TestCase(0)]
        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(23498)]
        public async Task execute_async_breaks_for_the_specified_delay(int delayInMs)
        {
            var delayService = new DelayServiceMock();
            var totalDelay = TimeSpan.Zero;

            delayService
                .When(x => x.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Do<TimeSpan, CancellationToken>((t, ct) => totalDelay += t)
                .Return(Task.FromResult(true));

            var sut = new WaitActionBuilder()
                .WithDelayService(delayService)
                .WithDelay(TimeSpan.FromMilliseconds(delayInMs))
                .Build();

            await sut.ExecuteAsync(new ExecutionContext());

            Assert.AreEqual(TimeSpan.FromMilliseconds(delayInMs), totalDelay);
        }

        [TestCase(850, 800, 50)]
        [TestCase(850, 849, 1)]
        [TestCase(3478, 2921, 557)]
        public async Task execute_async_skips_ahead_if_the_context_has_skip_ahead(int delayInMs, int skipInMs, int expectedDelayInMs)
        {
            var delayService = new DelayServiceMock();
            var totalDelay = TimeSpan.Zero;

            delayService
                .When(x => x.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Do<TimeSpan, CancellationToken>((t, ct) => totalDelay += t)
                .Return(Task.FromResult(true));

            var sut = new WaitActionBuilder()
                .WithDelayService(delayService)
                .WithDelay(TimeSpan.FromMilliseconds(delayInMs))
                .Build();

            await sut.ExecuteAsync(new ExecutionContext(TimeSpan.FromMilliseconds(skipInMs)));

            Assert.AreEqual(TimeSpan.FromMilliseconds(expectedDelayInMs), totalDelay);
        }

        [TestCase(850, 800)]
        [TestCase(850, 849)]
        [TestCase(3478, 2921)]
        public async Task execute_async_skips_ahead_if_the_context_has_skip_ahead_even_if_the_context_is_paused(int delayInMs, int skipInMs)
        {
            var delayService = new DelayServiceMock();
            var totalDelay = TimeSpan.Zero;

            delayService
                .When(x => x.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Do<TimeSpan, CancellationToken>((t, ct) => totalDelay += t)
                .Return(Task.FromResult(true));

            var sut = new WaitActionBuilder()
                .WithDelayService(delayService)
                .WithDelay(TimeSpan.FromMilliseconds(delayInMs))
                .Build();

            using (var context = new ExecutionContext(TimeSpan.FromMilliseconds(skipInMs)) { IsPaused = true })
            {
                var task = sut.ExecuteAsync(context);

                Assert.False(task.Wait(TimeSpan.FromMilliseconds(50)));

                await context
                    .WhenAnyValue(x => x.Progress)
                    .Where(x => x == TimeSpan.FromMilliseconds(skipInMs))
                    .FirstAsync()
                    .Timeout(TimeSpan.FromSeconds(3))
                    .ToTask();
            }
        }

        [Test]
        public async Task execute_async_reports_progress()
        {
            var delayService = new DelayServiceMock(MockBehavior.Loose);
            var sut = new WaitActionBuilder()
                .WithDelayService(delayService)
                .WithDelay(TimeSpan.FromMilliseconds(50))
                .Build();

            using (var context = new ExecutionContext())
            {
                Assert.AreEqual(TimeSpan.Zero, context.Progress);

                await sut.ExecuteAsync(context);

                Assert.AreEqual(TimeSpan.FromMilliseconds(50), context.Progress);
            }
        }

        [Test]
        public async Task execute_async_reports_progress_correctly_even_if_the_skip_ahead_exceeds_the_wait_duration()
        {
            var delayService = new DelayServiceMock(MockBehavior.Loose);
            var sut = new WaitActionBuilder()
                .WithDelayService(delayService)
                .WithDelay(TimeSpan.FromMilliseconds(50))
                .Build();

            using (var context = new ExecutionContext(TimeSpan.FromMilliseconds(100)))
            {
                Assert.AreEqual(TimeSpan.Zero, context.Progress);

                await sut.ExecuteAsync(context);

                Assert.AreEqual(TimeSpan.FromMilliseconds(50), context.Progress);
            }
        }

        [Test]
        public void execute_async_bails_out_if_context_is_cancelled()
        {
            var delayService = new DelayServiceMock();
            var delayCallCount = 0;

            using (var context = new ExecutionContext())
            {
                delayService
                    .When(x => x.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                    .Do(
                        () =>
                        {
                            if (delayCallCount++ == 2)
                            {
                                context.Cancel();
                            }
                        })
                    .Return(Task.FromResult(true));

                var sut = new WaitActionBuilder()
                    .WithDelayService(delayService)
                    .WithDelay(TimeSpan.FromSeconds(50))
                    .Build();

                Assert.Throws<OperationCanceledException>(async () => await sut.ExecuteAsync(context));
                Assert.True(context.IsCancelled);
            }
        }

        [Test]
        public async Task execute_async_pauses_if_context_is_paused()
        {
            var delayService = new DelayServiceMock();
            var delayCallCount = 0;

            using (var context = new ExecutionContext())
            {
                delayService
                    .When(x => x.DelayAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                    .Do(
                        () =>
                        {
                            if (delayCallCount++ == 2)
                            {
                                context.IsPaused = true;
                            }
                        })
                    .Return(Task.FromResult(true));

                var sut = new WaitActionBuilder()
                    .WithDelayService(delayService)
                    .WithDelay(TimeSpan.FromSeconds(50))
                    .Build();

                var task = sut.ExecuteAsync(context);
                Assert.False(task.Wait(TimeSpan.FromMilliseconds(50)));

                await context
                    .WhenAnyValue(x => x.IsPaused)
                    .Where(x => x)
                    .FirstAsync()
                    .Timeout(TimeSpan.FromSeconds(3))
                    .ToTask();

                Assert.True(context.IsPaused);
            }
        }
    }
}