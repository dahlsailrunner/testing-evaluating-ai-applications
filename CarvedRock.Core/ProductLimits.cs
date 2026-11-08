namespace CarvedRock.Core;

/// <summary>
/// Field limits for products. Shared so the product validator (Domain) and the AI description
/// drafting (Agent) enforce - and, for the AI, announce to the model - the very same numbers.
/// </summary>
public static class ProductLimits
{
    public const int MaxNameLength = 50;
    public const int MaxDescriptionLength = 150;
}
