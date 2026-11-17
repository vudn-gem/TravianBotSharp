namespace MainCore.UI.ViewModels.UserControls
{
    public sealed class EmptyAsZeroConverter : BindingTypeConverter<string, int>
    {
        public override int GetAffinityForObjects() => 100;

        public override bool TryConvert(string? from, object? conversionHint, out int result)
        {
            if (string.IsNullOrWhiteSpace(from))
            {
                result = 0;
                return true;
            }
            return int.TryParse(from, out result);
        }
    }
}
