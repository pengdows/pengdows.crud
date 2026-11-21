using System;

namespace pengdows.crud.@internal;

/// <summary>
/// The rule every parameter name must pass before it can be written into SQL text: an ASCII letter first, then ASCII
/// letters, digits or underscores, and no longer than the dialect's <c>ParameterNameMaxLength</c>. Used by the
/// container when a parameter is added and by the dialect when a placeholder is written, so the two cannot drift.
/// </summary>
/// <remarks>
/// A hand-written loop rather than a regex: the rule is small, and a match per parameter would cost more for no
/// extra safety.
/// </remarks>
internal static class ParameterNameRule
{
    internal static void Validate(string parameterName, int maxLength)
    {
        var valid = parameterName.Length is > 0 && parameterName.Length <= maxLength &&
                    IsAsciiLetter(parameterName[0]);
        for (var i = 1; valid && i < parameterName.Length; i++)
        {
            var c = parameterName[i];
            valid = IsAsciiLetter(c) || (c >= '0' && c <= '9') || c == '_';
        }

        if (!valid)
        {
            throw new ArgumentException(
                $"Parameter name '{parameterName}' is not allowed: it must start with an ASCII letter, be at most " +
                $"{maxLength} characters, and contain only ASCII letters, digits and underscores.",
                nameof(parameterName));
        }
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
}
