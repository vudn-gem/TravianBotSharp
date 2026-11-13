namespace MainCore.Services
{
    public static class ScheduledMonitoringSettings
    {
        public static void ConvertLegacyIntervals(Dictionary<AccountSettingEnums, int> settings)
        {
            Convert(settings, AccountSettingEnums.ScheduledMonitoringIntervalMin,
                AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes);
            Convert(settings, AccountSettingEnums.ScheduledMonitoringIntervalMax,
                AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes);
        }

        private static void Convert(Dictionary<AccountSettingEnums, int> settings,
            AccountSettingEnums legacy, AccountSettingEnums minutes)
        {
            if (!settings.ContainsKey(minutes) && settings.TryGetValue(legacy, out var seconds))
                settings[minutes] = seconds > 0 ? (int)(((long)seconds + 59) / 60) : 0;
        }
    }
}
