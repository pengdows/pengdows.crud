namespace pengdows.crud;

internal static class InternalSessionSettingsExtensions
{
    internal static string GetSessionSettingsPreamble(this IDatabaseContext context)
    {
        var baseSettings = context.GetBaseSessionSettings();
        if (!context.IsReadOnlyConnection)
        {
            return baseSettings;
        }

        return string.Concat(baseSettings, context.GetReadOnlySessionSettings());
    }
}
