using FluentResults;
using MainCore.Entities;
using MainCore.Models;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.Test.Parsers;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class ConquestTaskTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData(false, 4, 25)]
        [InlineData(true, 4, 25)]
        [InlineData(false, 44, 25)]
        [InlineData(false, 4, 44)]
        [InlineData(true, 44, 25)]
        [InlineData(false, 41, 25)]
        [InlineData(false, 4, 41)]
        [InlineData(true, 41, 25)]
        [InlineData(true, 41, 41)]
        [InlineData(true, 4, 4)]
        [InlineData(true, 99, 25)]
        [InlineData(false, 4, 25, true)]
        [InlineData(false, 4, 25, false, "late")]
        [InlineData(false, 4, 25, false, "save-overrun")]
        [InlineData(false, 4, 25, false, "save-fails")]
        [InlineData(false, 4, 25, false, "cancel")]
        [InlineData(false, 4, 25, false, "cancel-locked")]
        public async Task SendsPerWaveTargetsWithRandomFallback(
            bool unavailable, int firstTarget, int secondTarget, bool inputFails = false,
            string timingScenario = "")
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(3000);
            await page.SetContentAsync("""
                <div class="contentNavi subNavi">
                    <a class="tabItem" href="/build.php?id=39&amp;tt=2"
                       onclick="event.preventDefault(); window.sendTabOpened = true; document.querySelector('#build').hidden = false">Send troops</a>
                </div>
                <div id="build" hidden><div class="a2b"><form action="/build.php?id=39&amp;tt=2"></form></div></div>
                <script>
                window.waves = [];
                window.confirmed = false;
                window.unavailable = false;
                window.unavailableTarget = '4';
                const form = document.querySelector('form');
                function field(name, value) {
                    return `<input type="hidden" name="${name}" value="${value}">`;
                }
                function saveTargets() {
                    for (const select of form.querySelectorAll('select')) {
                        const match = select.name.match(/troops\[(\d+)\]\[catapultTarget(\d)\]/);
                        waves[Number(match[1])]['target' + match[2]] = select.value;
                    }
                }
                function entry() {
                    const additional = waves.length > 0;
                    if (additional) form.id = 'troopSendForm';
                    form.onsubmit = event => {
                        event.preventDefault();
                        if (!window.sendTabOpened) throw new Error('Send troops tab was not opened');
                        const attackType = additional
                            ? form.querySelector('input[type="hidden"][name="eventType"]')
                            : form.querySelector('input[name="eventType"]:checked');
                        if (attackType?.value !== '3')
                            throw new Error('Conquest must select Attack: Normal, not Raid');
                        const counts = Array.from({length: 11}, (_, i) =>
                            form.querySelector(`[name="troop[t${i + 1}]"]`)?.value || '0');
                        waves.push({ counts, target1: '99', target2: '' });
                        confirmPage();
                    };
                    const previous = waves.map((wave, index) =>
                        wave.counts.map((count, i) => field(`troops[${index}][t${i + 1}]`, count)).join('') +
                        field(`troops[${index}][villageId]`, '2')).join('');
                    form.innerHTML = previous + '<table id="troops"><tbody><tr><td>' +
                        Array.from({length: additional ? 10 : 11}, (_, i) =>
                        `<input name="troop[t${i + 1}]" value="${i === 2 ? '3' : i === 3 ? '' : '0'}" ${additional && i === 1 ? 'disabled' : ''}>`).join('') +
                        '</td></tr></tbody></table>' +
                        (additional
                            ? field('x', '-50') + field('y', '50') + field('eventType', '3') +
                                '<button type="submit" id="addAsWaveBtn">Add as wave</button>'
                            : '<input name="x"><input name="y"><input type="radio" name="eventType" value="3">' +
                                '<input type="radio" name="eventType" value="4" checked>' +
                                '<button type="submit">Continue</button>');
                }
                function targets(index, number) {
                    const options = [['99', 'Random'], ['4', 'Cropland'], ['25', 'Residence'], ['44', 'Command Center'], ['41', 'Horse Drinking Trough'], ['27', 'Treasury']];
                    return `<select name="troops[${index}][catapultTarget${number}]">` +
                        (number === 2 ? '<option value=""></option>' : '') +
                        options.map(([value, name]) =>
                            `<option value="${value}" ${unavailable && value === unavailableTarget ? 'disabled' : ''}>${name}</option>`).join('') +
                        '</select>';
                }
                function confirmPage() {
                    form.id = 'troopSendForm';
                    form.onsubmit = event => event.preventDefault();
                    form.innerHTML = field('eventType', '3') + field('x', '-50') + field('y', '50') +
                        waves.map((wave, index) => {
                            const prefix = `troops[${index}]`;
                            return wave.counts.map((count, i) => field(`${prefix}[t${i + 1}]`, count)).join('') +
                                field(`${prefix}[villageId]`, '2') +
                                field(`${prefix}[catapultTarget1]`, wave.target1) +
                                field(`${prefix}[catapultTarget2]`, wave.target2) +
                                (index === waves.length - 1 ? targets(index, 1) +
                                    (Number(wave.counts[7]) >= 20 ? targets(index, 2) : '') : '') +
                                `<table class="troop_details"><tbody class="infos"><tr><td>
                                 <span id="at" value="${Math.floor(Date.now()/1000) + 3600}"></span>
                                 </td></tr></tbody></table>`;
                        }).join('') +
                        '<button type="submit" id="back">Edit</button>' +
                        '<button type="submit" id="copyWave">Copy wave</button>' +
                        '<button type="submit" id="addWave">Add a wave</button>' +
                        '<button type="button" id="confirmSendTroops">Confirm</button>';
                    form.querySelector('#addWave').onclick = event => {
                        event.preventDefault(); saveTargets(); entry();
                    };
                    form.querySelector('#confirmSendTroops').onclick = () => {
                        saveTargets(); window.confirmed = true; window.confirmedAt = Date.now(); form.remove();
                    };
                }
                const getAttribute = Element.prototype.getAttribute;
                Element.prototype.getAttribute = function(name) {
                    if (this.id === 'at' && name === 'value') window.finalCheckAt = Date.now();
                    return getAttribute.call(this, name);
                };
                entry();
                </script>
                """);
            await page.EvaluateAsync("value => window.unavailable = value", unavailable);
            await page.EvaluateAsync("value => window.unavailableTarget = String(value)", firstTarget);
            var browser = Substitute.For<IChromeBrowser>();
            browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            browser.IsInitialized.Returns(true);
            browser.CurrentPage.Returns(page);
            browser.CurrentUrl.Returns("https://example.com/dorf1.php");
            browser.OpenNewTab().Returns(Result.Ok());
            browser.CloseNewTab().Returns(Result.Ok());
            browser.Navigate(Arg.Any<string>()).Returns(Result.Ok());
            var inputs = new List<(string? Name, string Value)>();
            browser.Input(Arg.Any<ILocator>(), Arg.Any<string>(), Arg.Any<string>()).Returns(async call =>
            {
                if (inputFails) return Result.Fail("Troop input failed.");
                var input = (ILocator)call[0];
                var value = (string)call[1];
                inputs.Add((await input.GetAttributeAsync("name"), value));
                await input.FillAsync(value);
                return Result.Ok();
            });

            var first = new long[11];
            first[0] = 19;
            first[7] = 20;
            var second = new long[11];
            second[0] = 19;
            second[7] = 1;
            var sendAt = DateTime.Now.AddSeconds(timingScenario == "late" ? 8 : 13);
            var task = new ConquestTask.Task(new AccountId(1), new VillageId(2), -50, 50,
                [new ConquestWave(first, firstTarget, secondTarget), new ConquestWave(second, 27)],
                DateTime.Now.AddHours(2), sendAt, 0);
            DateTime? lockedAt = null;
            task.Persist = () =>
            {
                if (task.Status == "Waiting for final check")
                {
                    if (timingScenario == "cancel" && !task.CancelRequested) task.RequestCancel();
                    return;
                }
                if (!task.ConfirmationAttempted) return;
                lockedAt = DateTime.Now;
                if (timingScenario == "save-fails") throw new InvalidOperationException("Save failed.");
                if (timingScenario == "cancel-locked") task.RequestCancel().ShouldBeFalse();
                Thread.Sleep(timingScenario == "save-overrun" ? 5500 : 200);
            };
            var handler = new ConquestTask.HandleBehavior(new ConquestTask(browser));

            if (timingScenario is "save-fails" or "cancel")
            {
                if (timingScenario == "save-fails")
                    await Should.ThrowAsync<InvalidOperationException>(async () =>
                        await handler.HandleAsync(task, task.CancelToken));
                else
                    await Should.ThrowAsync<OperationCanceledException>(async () =>
                        await handler.HandleAsync(task, task.CancelToken));
                task.ConfirmationAttempted.ShouldBe(timingScenario == "save-fails");
                (await page.EvaluateAsync<bool>("window.confirmed")).ShouldBeFalse();
                await browser.Received(1).CloseNewTab();
                return;
            }

            var result = await handler.HandleAsync(task, task.CancelToken);
            if (timingScenario is "late" or "save-overrun")
            {
                result.IsFailed.ShouldBeTrue();
                result.Errors.ShouldContain(error => error.Message.Contains(
                    timingScenario == "late" ? "ten seconds" : "send deadline"));
                task.ConfirmationAttempted.ShouldBe(timingScenario == "save-overrun");
                (await page.EvaluateAsync<bool>("window.confirmed")).ShouldBeFalse();
                await browser.Received(1).CloseNewTab();
                return;
            }
            if (inputFails)
            {
                result.IsFailed.ShouldBeTrue();
                task.ConfirmationAttempted.ShouldBeFalse();
                (await page.EvaluateAsync<bool>("window.confirmed")).ShouldBeFalse();
                (await page.EvaluateAsync<int>("waves.length")).ShouldBe(0);
                await browser.Received(1).CloseNewTab();
                return;
            }
            Received.InOrder(() =>
            {
                browser.Navigate("https://example.com/dorf2.php?newdid=2");
                browser.Navigate("https://example.com/build.php?id=39");
            });
            var randomUnavailable = unavailable && firstTarget == 99;
            result.IsSuccess.ShouldBe(!randomUnavailable);
            (await page.EvaluateAsync<bool>("window.confirmed")).ShouldBe(!randomUnavailable);
            await browser.Received(1).CloseNewTab();
            if (randomUnavailable)
            {
                task.ConfirmationAttempted.ShouldBeFalse();
                result.Errors.ShouldContain(error => error.Message.Contains("unavailable"));
                return;
            }

            lockedAt.ShouldNotBeNull();
            (lockedAt.Value - sendAt).TotalSeconds.ShouldBeInRange(-5, -4.25);
            var finalCheckAt = await page.EvaluateAsync<double>("window.finalCheckAt");
            var sendAtMilliseconds = new DateTimeOffset(sendAt).ToUnixTimeMilliseconds();
            (finalCheckAt - sendAtMilliseconds).ShouldBeInRange(-10000, -8500);
            var confirmedAt = await page.EvaluateAsync<double>("window.confirmedAt");
            (confirmedAt - sendAtMilliseconds).ShouldBeInRange(0, 750);
            task.ConfirmationAttempted.ShouldBeTrue();
            task.CancelRequested.ShouldBeFalse();
            (await page.EvaluateAsync<string[]>("waves.map(wave => wave.target1)"))
                .ShouldBe([unavailable ? "99" : firstTarget.ToString(), "27"]);
            (await page.EvaluateAsync<string[]>("waves.map(wave => wave.target2)"))
                .ShouldBe([unavailable && secondTarget == firstTarget ? "99" : secondTarget.ToString(), ""]);
            task.Waves[0].CatapultTarget1.ShouldBe(firstTarget);
            task.Waves[0].CatapultTarget2.ShouldBe(secondTarget);
            (await page.EvaluateAsync<string[]>("waves.map(wave => wave.counts[7])")).ShouldBe(["20", "1"]);
            inputs.ShouldBe([
                ("troop[t1]", "19"), ("troop[t3]", ""), ("troop[t8]", "20"), ("x", "-50"), ("y", "50"),
                ("troop[t1]", "19"), ("troop[t3]", ""), ("troop[t8]", "1")
            ]);
        }
    }
}
