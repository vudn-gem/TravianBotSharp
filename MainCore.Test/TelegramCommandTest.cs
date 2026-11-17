using System.Net;
using System.Text.Json;

using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public sealed class TelegramCommandTest : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-commands-{Guid.NewGuid():N}");
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly TaskManager _manager;
        private readonly TelegramCommandDispatcher _dispatcher;
        private readonly TelegramCommandCursorStore _cursors;
        private readonly TelegramCommandReceiver _receiver;
        private readonly TelegramSender _sender;
        private readonly RecordingHandler _http = new();
        private readonly DbContextOptions<AppDbContext> _options;

        private static TelegramCredentials Credentials => new()
        {
            BotToken = "123:fake-token", ChatId = "456", EnableCommands = true,
        };

        public TelegramCommandTest()
        {
            _connection.Open();
            _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            using (var context = new AppDbContext(_options))
            {
                context.Database.EnsureCreated();
                context.Accounts.AddRange(
                    new Account { Id = 1, Username = "one", Server = "https://example.com" },
                    new Account { Id = 2, Username = "two", Server = "https://example.com" });
                context.FarmLists.AddRange(
                    new Farm { Id = 10, AccountId = 1, Name = "home", IsActive = true },
                    new Farm { Id = 20, AccountId = 2, Name = "other", IsActive = true });
                context.HeroOasisSettings.Add(new HeroOasisSetting { FarmId = 10 });
                context.SaveChanges();
            }
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(_options));
            var logger = Substitute.For<Serilog.ILogger>();
            var outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var rx = new RxQueue(factory);
            _manager = new TaskManager(rx, factory, logger,
                new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, TimeProvider.System));
            _dispatcher = new TelegramCommandDispatcher(factory, _manager, rx);
            _cursors = new TelegramCommandCursorStore(Path.Combine(_directory, "cursor.json"));
            _cursors.Save(Credentials, 0);
            _sender = new TelegramSender(new HttpClient(_http));
            _receiver = new TelegramCommandReceiver(_cursors, _dispatcher, _sender, TimeProvider.System, logger);
        }

        [Fact]
        public async Task HelpAndQueriesExposeSavedIdsWithoutChangingQueue()
        {
            var help = await Execute("/tbs-command");
            foreach (var command in new[] { "/accounts", "/status", "/farm-lists", "/hero-farm", "/pause",
                "/resume", "/start-farm", "/stop-farm" })
                help.ShouldContain(command);
            foreach (var command in new[] { "/active <account-id> <farm-list-id>", "/deactive <account-id> <farm-list-id>",
                "/active-oasis <account-id> <farm-list-id>", "/deactive-oasis <account-id> <farm-list-id>",
                "/check-oasis <account-id> <farm-list-id>" })
                help.ShouldContain(command);
            (await Execute("/tbs_command")).ShouldBe(help);
            (await Execute("/accounts")).ShouldContain("1: one - Offline");
            (await Execute("/farm-lists 1")).ShouldContain("10: home - Active");
            (await Execute("/farm-lists 1")).ShouldNotContain("20: other");
            (await Execute("/status 1")).ShouldContain("Tasks: 0");
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
        }

        [Theory]
        [InlineData("/hero-farm 1", "Usage:")]
        [InlineData("/hero-farm 1 10 extra", "Usage:")]
        [InlineData("/pause nope", "positive integers")]
        [InlineData("/status -1", "positive integers")]
        [InlineData("/status 2147483648", "positive integers")]
        [InlineData("/status 999", "Account not found")]
        [InlineData("/hero-farm 1 20", "does not belong")]
        [InlineData("/restart 1", "Unknown command")]
        [InlineData("/active", "Usage:")]
        [InlineData("/deactive 1 10 extra", "Usage:")]
        [InlineData("/active 10", "Usage:")]
        [InlineData("/deactive 10", "Usage:")]
        [InlineData("/active-oasis 10", "Usage:")]
        [InlineData("/deactive-oasis 10", "Usage:")]
        [InlineData("/active-oasis 1 -10", "positive integers")]
        [InlineData("/deactive-oasis 1 2147483648", "positive integers")]
        [InlineData("/active nope 10", "positive integers")]
        [InlineData("/deactive 1 999", "Farm list not found")]
        [InlineData("/active-oasis 1 999", "Farm list not found")]
        [InlineData("/active 999 10", "Account not found")]
        [InlineData("/check-oasis 1", "Usage:")]
        [InlineData("/check-oasis 1 nope", "positive integers")]
        public async Task RejectsInvalidCommandsWithoutQueueing(string command, string expected)
        {
            (await Execute(command)).ShouldContain(expected);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            _manager.GetTaskList(new(2)).ShouldBeEmpty();
        }

        [Fact]
        public async Task HeroUsesSavedSettingsAndRejectsDuplicateAndInvalidSettings()
        {
            (await Execute("/hero-farm 1 10")).ShouldContain("Queued Hero clear oasis");
            _manager.GetTaskList(new(1)).OfType<HeroClearFarmOasisTask.Task>().Single().FarmId.ShouldBe(new FarmId(10));
            (await Execute("/hero_farm 1 10")).ShouldContain("already exists");
            _manager.GetTaskList(new(1)).Count.ShouldBe(1);
            _manager.Remove<HeroClearFarmOasisTask.Task>(new(1));
            using var context = new AppDbContext(_options);
            context.HeroOasisSettings.Single().MinHeroHealth = 101;
            context.SaveChanges();
            (await Execute("/hero-farm 1 10")).ShouldContain("Invalid saved hero settings");
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
        }

        [Fact]
        public async Task ActiveAndDeactiveSetStateIdempotentlyWithoutStartingFarming()
        {
            using var context = new AppDbContext(_options);
            foreach (var command in new[] { "/deactive 1 10", "/deactive 1 10" })
            {
                (await Execute(command)).ShouldContain("Inactive (account 1)");
                context.FarmLists.AsNoTracking().Single(x => x.Id == 10).IsActive.ShouldBeFalse();
                context.FarmLists.AsNoTracking().Single(x => x.Id == 20).IsActive.ShouldBeTrue();
            }
            foreach (var command in new[] { "/active 1 10", "/active 1 10" })
            {
                (await Execute(command)).ShouldContain("Active (account 1)");
                context.FarmLists.AsNoTracking().Single(x => x.Id == 10).IsActive.ShouldBeTrue();
            }
            (await Execute("/deactive 2 20")).ShouldContain("account 2");
            context.FarmLists.AsNoTracking().Single(x => x.Id == 20).IsActive.ShouldBeFalse();
            context.FarmLists.AsNoTracking().Single(x => x.Id == 10).IsActive.ShouldBeTrue();
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            _manager.GetTaskList(new(2)).ShouldBeEmpty();
        }

        [Theory]
        [InlineData("/active-oasis", true, StatusEnums.Offline)]
        [InlineData("/active_oasis", true, StatusEnums.Paused)]
        [InlineData("/deactive-oasis", false, StatusEnums.Online)]
        [InlineData("/deactive_oasis", false, StatusEnums.Offline)]
        [InlineData("/check-oasis", false, StatusEnums.Online)]
        [InlineData("/check_oasis", false, StatusEnums.Paused)]
        public async Task OasisCommandsUseOwnerAndSavedSettingsWithoutChangingLocalActiveFlag(
            string command, bool activate, StatusEnums status)
        {
            using var context = new AppDbContext(_options);
            context.FarmOasisSettings.Add(new FarmOasisSetting
            {
                FarmId = 20, CheckAllowedAnimals = 3, SafeAnimal = 5,
            });
            context.SaveChanges();
            _manager.SetStatus(new(2), status);
            (await Execute($"{command} 2 20")).ShouldContain("farm list 20, account 2");
            var task = _manager.GetTaskList(new(2)).OfType<CheckFarmTargetOasisTask.Task>().Single();
            task.AccountId.ShouldBe(new AccountId(2));
            task.FarmId.ShouldBe(new FarmId(20));
            task.Activate.ShouldBe(activate);
            task.Synchronize.ShouldBe(command.Replace('_', '-') == "/check-oasis");
            var scheduled = task.ExecuteAt = DateTime.Now.AddHours(1);
            (await Execute($"{command} 2 20")).ShouldContain("already queued");
            task.ExecuteAt.ShouldBe(scheduled);
            _manager.GetTaskList(new(2)).Count.ShouldBe(1);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            context.FarmLists.AsNoTracking().Single(x => x.Id == 20).IsActive.ShouldBeTrue();
            var saved = context.FarmOasisSettings.AsNoTracking().Single();
            saved.CheckAllowedAnimals.ShouldBe(3);
            saved.SafeAnimal.ShouldBe(5);
        }

        [Theory]
        [InlineData("/active-oasis")]
        [InlineData("/deactive-oasis")]
        [InlineData("/check-oasis")]
        public async Task OasisCommandsRejectMissingSettingsAndTransitionStates(string command)
        {
            (await Execute($"{command} 1 10")).ShouldContain("Save Check oasis settings");
            foreach (var status in new[] { StatusEnums.Starting, StatusEnums.Pausing, StatusEnums.Stopping })
            {
                _manager.SetStatus(new(1), status);
                (await Execute($"{command} 1 10")).ShouldContain("Wait and try again");
            }
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
        }

        [Theory]
        [InlineData("/active")]
        [InlineData("/deactive")]
        [InlineData("/active-oasis")]
        [InlineData("/deactive-oasis")]
        [InlineData("/check-oasis")]
        public async Task FarmCommandsRejectAnotherAccountsFarmList(string command)
        {
            (await Execute($"{command} 1 20")).ShouldContain("Farm list not found for this account");
            using var context = new AppDbContext(_options);
            context.FarmLists.AsNoTracking().Single(x => x.Id == 20).IsActive.ShouldBeTrue();
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            _manager.GetTaskList(new(2)).ShouldBeEmpty();
        }

        [Fact]
        public async Task FarmStartIsIdempotentAndStopDoesNotRemoveOtherTasks()
        {
            (await Execute("/start-farm 1")).ShouldContain("Queued Start farm list");
            var farm = _manager.GetTaskList(new(1)).OfType<StartFarmListTask.Task>().Single();
            farm.ExecuteAt = DateTime.Now.AddHours(1);
            var scheduled = farm.ExecuteAt;
            (await Execute("/start-farm 1")).ShouldContain("already queued");
            farm.ExecuteAt.ShouldBe(scheduled);
            _manager.Add(new StartAdventureTask.Task(new(1)));
            _manager.Add(new StartFarmListTask.Task(new(2)));
            (await Execute("/stop-farm 1")).ShouldContain("Removed Start farm list");
            _manager.GetTaskList(new(1)).Single().ShouldBeOfType<StartAdventureTask.Task>();
            _manager.GetTaskList(new(2)).Single().ShouldBeOfType<StartFarmListTask.Task>();
        }

        [Fact]
        public async Task StartFarmRequiresActiveListOrStartAll()
        {
            using var context = new AppDbContext(_options);
            context.FarmLists.Where(x => x.AccountId == 1).ExecuteUpdate(x => x.SetProperty(f => f.IsActive, false));
            (await Execute("/start-farm 1")).ShouldContain("No active farm lists");
            context.AccountsSetting.Add(new AccountSetting
            {
                AccountId = 1, Setting = AccountSettingEnums.UseStartAllButton, Value = 1,
            });
            context.SaveChanges();
            (await Execute("/start-farm 1")).ShouldContain("Queued Start farm list");
        }

        [Fact]
        public async Task PauseResumeRespectStatusAndPreserveTasks()
        {
            var id = new AccountId(1);
            (await Execute("/resume 1")).ShouldContain("Cannot resume");
            _manager.SetStatus(id, StatusEnums.Online);
            _manager.Add(new StartAdventureTask.Task(id));
            (await Execute("/pause 1")).ShouldContain("paused");
            _manager.GetStatus(id).ShouldBe(StatusEnums.Paused);
            (await Execute("/resume 1")).ShouldContain("resumed");
            _manager.GetStatus(id).ShouldBe(StatusEnums.Online);
            _manager.GetTaskList(id).Count.ShouldBe(1);
            _manager.SetStatus(id, StatusEnums.Stopping);
            (await Execute("/resume 1")).ShouldContain("Wait and try again");
            (await Execute("/start-farm 1")).ShouldContain("Wait and try again");
        }

        [Fact]
        public async Task PauseCancelsCurrentTaskBeforeReportingPaused()
        {
            var id = new AccountId(1);
            _manager.SetStatus(id, StatusEnums.Online);
            var task = new StartAdventureTask.Task(id) { Stage = StageEnums.Executing };
            _manager.Add(task);
            var queue = _manager.GetTaskQueue(id);
            using var cancellation = new CancellationTokenSource();
            queue.CancellationTokenSource = cancellation;
            using var registration = cancellation.Token.Register(() => task.Stage = StageEnums.Waiting);
            (await Execute("/pause 1")).ShouldContain("paused");
            cancellation.IsCancellationRequested.ShouldBeTrue();
            task.Stage.ShouldBe(StageEnums.Waiting);
            _manager.GetStatus(id).ShouldBe(StatusEnums.Paused);
            _manager.GetTaskList(id).Single().ShouldBeSameAs(task);
        }

        [Theory]
        [InlineData("wrong-chat")]
        [InlineData("group")]
        [InlineData("wrong-user")]
        [InlineData("bot")]
        [InlineData("forward")]
        [InlineData("via-bot")]
        [InlineData("disabled")]
        [InlineData("edited")]
        public async Task IgnoresUnauthorizedOrUnsupportedMessages(string scenario)
        {
            var update = Update(1, "/start-farm 1");
            var credentials = Credentials;
            switch (scenario)
            {
                case "wrong-chat": update.Message!.Chat!.Id = 999; break;
                case "group": update.Message!.Chat!.Type = "group"; break;
                case "wrong-user": update.Message!.From!.Id = 999; break;
                case "bot": update.Message!.From!.IsBot = true; break;
                case "forward": update.Message!.ForwardOrigin = new object(); break;
                case "via-bot": update.Message!.ViaBot = new object(); break;
                case "disabled": credentials.EnableCommands = false; break;
                case "edited": update.Message = null; break;
            }
            await _receiver.Process(credentials, update, CancellationToken.None);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            _http.Bodies.ShouldBeEmpty();
        }

        [Fact]
        public async Task CursorSurvivesRestartAndPreventsCommandReplay()
        {
            await _receiver.Process(Credentials, Update(1, "/start-farm 1"), CancellationToken.None);
            await Execute("/stop-farm 1");
            var restored = new TelegramCommandCursorStore(Path.Combine(_directory, "cursor.json"));
            restored.Load(Credentials).ShouldBe(2);
            var receiver = new TelegramCommandReceiver(restored, _dispatcher, _sender, TimeProvider.System,
                Substitute.For<Serilog.ILogger>());
            await receiver.Process(Credentials, Update(1, "/start-farm 1"), CancellationToken.None);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            _http.Bodies.Count.ShouldBe(1);
        }

        [Fact]
        public async Task ClaimsCommandBeforeExecutionAndDoesNotReplayWhenReplyFails()
        {
            _http.Fail = true;
            await _receiver.Process(Credentials, Update(1, "/start-farm 1"), CancellationToken.None);
            await Execute("/stop-farm 1");
            await _receiver.Process(Credentials, Update(1, "/start-farm 1"), CancellationToken.None);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            _cursors.Load(Credentials).ShouldBe(2);
        }

        [Fact]
        public async Task OldCommandsAreRejectedWithReply()
        {
            var update = Update(1, "/start-farm 1");
            update.Message!.Date -= 121;
            await _receiver.Process(Credentials, update, CancellationToken.None);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            _http.Bodies.Single().ShouldContain("expired");
        }

        [Fact]
        public async Task CursorWriteFailureDoesNotExecuteCommand()
        {
            var path = Path.Combine(_directory, "cursor.json.tmp");
            Directory.CreateDirectory(path);
            await Should.ThrowAsync<UnauthorizedAccessException>(() =>
                _receiver.Process(Credentials, Update(1, "/start-farm 1"), CancellationToken.None));
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
        }

        [Fact]
        public void CredentialsDefaultToReadOnlyAndRejectGroupsForCommands()
        {
            new TelegramCredentials().EnableCommands.ShouldBeFalse();
            var credentials = Credentials;
            credentials.ChatId = "-456";
            credentials.IsValid.ShouldBeTrue();
            credentials.CanReceiveCommands.ShouldBeFalse();
        }

        [Fact]
        public void LongRepliesAreChunkedWithinTelegramLimit()
        {
            var text = string.Join("\n", Enumerable.Repeat(new string('x', 199), 50));
            var chunks = TelegramCommandReceiver.ChunkReply(text).ToList();
            chunks.Count.ShouldBeGreaterThan(1);
            chunks.ShouldAllBe(x => x.Length <= 3500);
            string.Join("\n", chunks).ShouldBe(text);
        }

        [Fact]
        public async Task NewSessionDiscardsPendingCommandsAndAcceptsOnlyNewUpdates()
        {
            _receiver.BeginSession(Credentials, [Update(99, "/start-farm 1")]);
            await _receiver.Process(Credentials, Update(99, "/start-farm 1"), CancellationToken.None);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            await _receiver.Process(Credentials, Update(100, "/start-farm 1"), CancellationToken.None);
            _manager.GetTaskList(new(1)).Single().ShouldBeOfType<StartFarmListTask.Task>();
            _cursors.Load(Credentials).ShouldBe(101);
        }

        [Fact]
        public async Task CorruptCursorFailsClosed()
        {
            File.WriteAllText(Path.Combine(_directory, "cursor.json"), "invalid json");
            await Should.ThrowAsync<JsonException>(() =>
                _receiver.Process(Credentials, Update(1, "/start-farm 1"), CancellationToken.None));
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
        }

        private Task<string> Execute(string command) => _dispatcher.Execute(command, CancellationToken.None);

        private static TelegramUpdate Update(long id, string text) => new()
        {
            Id = id,
            Message = new TelegramCommandMessage
            {
                Text = text, Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Chat = new TelegramCommandChat { Id = 456, Type = "private" },
                From = new TelegramCommandUser { Id = 456 },
            },
        };

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public bool Fail { get; set; }
            public List<string> Bodies { get; } = [];
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { ok = !Fail })),
                };
            }
        }

        public void Dispose()
        {
            _sender.Dispose();
            _connection.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
