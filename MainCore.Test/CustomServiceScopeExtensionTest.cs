using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Entities;
using MainCore.Services;
using MainCore.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MainCore.Test
{
    public sealed class CustomServiceScopeExtensionTest
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ExecutesConquestTaskWithItsHandler(bool success)
        {
            var handler = Substitute.For<IHandler<ConquestTask.Task, Result>>();
            var expected = success ? Result.Ok() : Result.Fail("handler result");
            handler.HandleAsync(Arg.Any<ConquestTask.Task>(), Arg.Any<CancellationToken>())
                .Returns(expected);
            using var provider = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
            using var scope = provider.CreateScope();
            using var cancellation = new CancellationTokenSource();
            var arrival = DateTime.Now.AddHours(2);
            var task = new ConquestTask.Task(new AccountId(1), new VillageId(30943), -3, 34,
                [new MainCore.Models.ConquestWave([19, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0])],
                arrival, arrival.AddHours(-1), 0.4);

            var result = await scope.Execute(task, cancellation.Token);

            result.ShouldBeSameAs(expected);
            await handler.Received(1).HandleAsync(task, cancellation.Token);
        }

        [Fact]
        public async Task ExecutesHeroClearOasisTaskWithItsHandler()
        {
            var handler = Substitute.For<IHandler<HeroClearFarmOasisTask.Task, Result>>();
            var expected = Result.Ok();
            handler.HandleAsync(Arg.Any<HeroClearFarmOasisTask.Task>(), Arg.Any<CancellationToken>())
                .Returns(expected);
            using var provider = new ServiceCollection().AddSingleton(handler).BuildServiceProvider();
            using var scope = provider.CreateScope();
            using var cancellation = new CancellationTokenSource();
            var task = new HeroClearFarmOasisTask.Task(new AccountId(1), new FarmId(7520));

            var result = await scope.Execute(task, cancellation.Token);

            result.ShouldBeSameAs(expected);
            await handler.Received(1).HandleAsync(task, cancellation.Token);
        }

        [Fact]
        public async Task ExecutesClearOasisTaskWithItsHandler()
        {
            var handler = Substitute.For<IHandler<ClearFarmOasisTask.Task, Result>>();
            var expected = Result.Fail("handler result");
            handler.HandleAsync(Arg.Any<ClearFarmOasisTask.Task>(), Arg.Any<CancellationToken>())
                .Returns(expected);
            using var provider = new ServiceCollection()
                .AddSingleton(handler)
                .BuildServiceProvider();
            using var scope = provider.CreateScope();
            using var cancellation = new CancellationTokenSource();
            var task = new ClearFarmOasisTask.Task(new AccountId(1), new FarmId(7520))
            {
                ResumeTargetIndex = 3,
            };

            var result = await scope.Execute(task, cancellation.Token);

            result.ShouldBeSameAs(expected);
            await handler.Received(1).HandleAsync(task, cancellation.Token);
        }
    }
}
