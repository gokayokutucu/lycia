namespace Sample_Net90.Choreography.Domain.Constants;

public static class ValidationErrors
{
    public const string IsRequired = "{PropertyName} is required.";
    public const string MustBeCorrectFormat = "{PropertyName} must be in a correct format.";
    public const string MustNotBeEmpty = "{PropertyName} must have at least one item.";
    public const string MustBeGreaterThan = "{PropertyName} must be greater than {ComparisonValue}.";
}
