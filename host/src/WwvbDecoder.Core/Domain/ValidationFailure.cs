namespace WwvbDecoder.Core;

/// <summary>
/// One failed validation check in a WWVB frame.
/// </summary>
/// <param name="Check">The specific validation check that failed.</param>
/// <param name="Detail">Display text for people, for example <c>second 19 is Error</c>. The wording
/// may change; code should use <see cref="Check"/>.</param>
public sealed record ValidationFailure(ValidationCheck Check, string Detail);
