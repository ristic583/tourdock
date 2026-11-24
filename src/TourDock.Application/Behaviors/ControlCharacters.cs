using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace TourDock.Application.Behaviors;

/// <summary>
/// Finds text with control characters (NUL and the like) anywhere in a request. PostgreSQL can't store NUL in text
/// and such input is never legitimate, so it gets a 400 instead of failing deep down as a 500. Tabs and line breaks
/// are allowed.
/// </summary>
internal static class ControlCharacters
{
    public const string Message = "Must not contain control characters.";

    private const int MaxDepth = 3;

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    public static IEnumerable<string> FindIn(object? request) => Find(request, prefix: null, depth: 0);

    private static IEnumerable<string> Find(object? value, string? prefix, int depth)
    {
        if (value is null || depth > MaxDepth)
        {
            yield break;
        }

        foreach (var property in Properties.GetOrAdd(value.GetType(), Readable))
        {
            var name = prefix is null ? property.Name : $"{prefix}.{property.Name}";
            var item = property.GetValue(value);
            if (item is string text)
            {
                if (HasControlCharacter(text))
                {
                    yield return name;
                }
            }
            else if (item is IEnumerable list and not string)
            {
                if (list.OfType<string>().Any(HasControlCharacter))
                {
                    yield return name;
                }
            }
            else if (item is not null && IsOwnType(item.GetType()))
            {
                foreach (var nested in Find(item, name, depth + 1))
                {
                    yield return nested;
                }
            }
        }
    }

    private static bool HasControlCharacter(string text) =>
        text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r'));

    // Only our own request/DTO records are walked; never streams or framework types.
    private static bool IsOwnType(Type type) =>
        type.IsClass && type.Namespace?.StartsWith("TourDock.", StringComparison.Ordinal) == true;

    private static PropertyInfo[] Readable(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !typeof(Stream).IsAssignableFrom(p.PropertyType))
            .ToArray();
}
