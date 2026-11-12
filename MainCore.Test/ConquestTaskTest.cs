using FluentResults;
using MainCore.Entities;
using MainCore.Models;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.Test.Parsers;

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
        public async Task SendsPerWaveTargetsWithRandomFallback(bool unavailable, int firstTarget, int secondTarget)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(3000);
            await page.SetContentAsync("""
                <form id="troopSendForm"></form>
                <script>
                window.waves = [];
                window.confirmed = false;
                window.unavailable = false;
                window.unavailableTarget = '4';
                const form = document.querySelector('#troopSendForm');
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
                    form.onsubmit = event => {
                        event.preventDefault();
                        if (form.querySelector('input[name="eventType"]:checked')?.value !== '3')
                            throw new Error('Conquest must select Attack: Normal, not Raid');
                        const counts = Array.from({length: 11}, (_, i) =>
                            form.querySelector(`[name="troop[t${i + 1}]"]`).value);
                        waves.push({ counts, target1: '99', target2: '' });
                        confirmPage();
                    };
                    form.innerHTML = Array.from({length: 11}, (_, i) =>
                        `<input name="troop[t${i + 1}]" value="0">`).join('') +
                        '<input name="x"><input name="y"><input type="radio" name="eventType" value="3">' +
                        '<input type="radio" name="eventType" value="4" checked>' +
                        '<button type="submit">Continue</button>';
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
                        saveTargets(); window.confirmed = true; form.remove();
                    };
                }
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

            var first = new long[11];
            first[0] = 19;
            first[7] = 20;
            var second = new long[11];
            second[0] = 19;
            second[7] = 1;
            var task = new ConquestTask.Task(new AccountId(1), new VillageId(2), -50, 50,
                [new ConquestWave(first, firstTarget, secondTarget), new ConquestWave(second, 27)],
                DateTime.Now.AddHours(2), DateTime.Now.AddSeconds(-1), 0);
            var handler = new ConquestTask.HandleBehavior(new ConquestTask(browser));

            var result = await handler.HandleAsync(task, CancellationToken.None);
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

            (await page.EvaluateAsync<string[]>("waves.map(wave => wave.target1)"))
                .ShouldBe([unavailable ? "99" : firstTarget.ToString(), "27"]);
            (await page.EvaluateAsync<string[]>("waves.map(wave => wave.target2)"))
                .ShouldBe([unavailable && secondTarget == firstTarget ? "99" : secondTarget.ToString(), ""]);
            task.Waves[0].CatapultTarget1.ShouldBe(firstTarget);
            task.Waves[0].CatapultTarget2.ShouldBe(secondTarget);
            (await page.EvaluateAsync<string[]>("waves.map(wave => wave.counts[7])")).ShouldBe(["20", "1"]);
        }
    }
}
