namespace MainCore.UI.ViewModels.Tabs
{
    public static class AccountSettingViewModelExtension
    {
        extension(AppDbContext context)
        {
            public void TriggerTask(TaskManager taskManager, AccountId accountId, Dictionary<AccountSettingEnums, int> settings, ReportMonitor reportMonitor, IncomingTroopsMonitor incomingTroopsMonitor, bool reportFeaturesChanged = false)
            {
                if (settings.Count == 0) return;
                if (settings.TryGetValue(AccountSettingEnums.EnableIncomingTroopsNotifications, out var incoming))
                {
                    if (incoming == 0)
                    {
                        incomingTroopsMonitor.Reset(accountId);
                        taskManager.Remove<CheckIncomingTroopsTask.Task>(accountId);
                    }
                    else if (!taskManager.IsExist<CheckIncomingTroopsTask.Task>(accountId))
                    {
                        taskManager.ScheduleIncomingScan(accountId, incomingTroopsMonitor.GetFallbackExecuteAt(accountId));
                    }
                }

                if (reportFeaturesChanged ||
                    (settings.TryGetValue(AccountSettingEnums.EnableReportNotifications, out var reportNotifications) &&
                    reportNotifications == 0 &&
                    settings.GetValueOrDefault(AccountSettingEnums.EnableAutoDeactivateLostReportTargets) == 0))
                {
                    reportMonitor.Reset(accountId);
                }

                if (settings.ContainsKey(AccountSettingEnums.EnableAutoLoadVillageBuilding))
                {
                    if (settings[AccountSettingEnums.EnableAutoLoadVillageBuilding] == 1)
                    {
                        var villages = context.Villages
                            .Where(x => x.AccountId == accountId.Value)
                            .Where(x => x.Buildings.Count < 40)
                            .Select(x => new VillageId(x.Id))
                            .ToList();

                        foreach (var village in villages)
                        {
                            var task = new UpdateBuildingTask.Task(accountId, village);
                            if (!taskManager.IsExist<UpdateBuildingTask.Task>(accountId, village))
                            {
                                taskManager.Add(task);
                            }
                        }
                    }
                    else
                    {
                        var villages = context.Villages
                            .Where(x => x.AccountId == accountId.Value)
                            .Select(x => new VillageId(x.Id))
                            .ToList();
                        foreach (var village in villages)
                        {
                            taskManager.Remove<InstantUpgradeTask.Task>(accountId, village);
                        }
                    }
                }

                if (settings.ContainsKey(AccountSettingEnums.EnableAutoStartAdventure))
                {
                    if (settings[AccountSettingEnums.EnableAutoStartAdventure] == 1)
                    {
                        var task = new StartAdventureTask.Task(accountId);
                        if (task.CanStart(context) && !taskManager.IsExist<StartAdventureTask.Task>(accountId))
                        {
                            taskManager.Add(task);
                        }
                    }
                    else
                    {
                        taskManager.Remove<StartAdventureTask.Task>(accountId);
                    }
                }

                if (settings.ContainsKey(AccountSettingEnums.EnableAutoClaimDailyQuest))
                {
                    if (settings[AccountSettingEnums.EnableAutoClaimDailyQuest] == 1)
                    {
                        var task = new ClaimDailyQuestTask.Task(accountId);
                        if (task.CanStart(context) && !taskManager.IsExist<ClaimDailyQuestTask.Task>(accountId))
                        {
                            taskManager.Add(task);
                        }
                    }
                    else
                    {
                        taskManager.Remove<ClaimDailyQuestTask.Task>(accountId);
                    }
                }

                if (settings.ContainsKey(AccountSettingEnums.EnableAutoSetHeroPoint))
                {
                    if (settings[AccountSettingEnums.EnableAutoSetHeroPoint] == 1)
                    {
                        var task = new SetHeroPointTask.Task(accountId);
                        if (task.CanStart(context) && !taskManager.IsExist<SetHeroPointTask.Task>(accountId))
                        {
                            taskManager.Add(task);
                        }
                    }
                    else
                    {
                        taskManager.Remove<SetHeroPointTask.Task>(accountId);
                    }
                }
            }
        }
    }
}