using System.Collections;
using System.Text.RegularExpressions;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Implements a path evaluator for attribute paths
/// </summary>
public static class RtPathEvaluator
{
    // Regex for splitting the terms:
    // - (?<property>[^\.\[\]]+) captures properties (anything except a dot or square brackets)
    // - (?<arrayIndex>-?\d+|\*) captures array accesses (a number, optionally with a minus sign, or the wildcard *)
    private static readonly Regex Regex = new( @"(?:^|\.)(?<property>[^\.\[\]]+)|\[(?<arrayIndex>-?\d+|\*)\]");


    /// <summary>
    /// Gets the value of an attribute path
    /// </summary>
    /// <param name="root">The root object</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <returns>The value of the attribute path</returns>
    public static object? GetValue(RtTypeWithAttributes root, string path)
    {
        var tokens = TokenizePath(path);
        return EvaluatePath(root, tokens);
    }

    /// <summary>
    /// Gets the value of an attribute path
    /// </summary>
    /// <param name="root">The root object</param>
    /// <param name="path">Path of attributes to be evaluated</param>
    /// <returns>The value of the attribute path</returns>
    public static object? GetValue(RtTypeWithAttributes root, IEnumerable<PathTerm> path)
    {
        return EvaluatePath(root, path.ToList());
    }

    /// <summary>
    /// Tokenizes a path into a list of path terms
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    // ReSharper disable once MemberCanBePrivate.Global
    public static List<PathTerm> TokenizePath(string path)
    {
        var tokens = new List<PathTerm>();
        foreach (Match match in Regex.Matches(path))
        {
            // If the group name "property" contains a value, it is a property name.
            if (match.Groups["property"].Success)
            {
                tokens.Add(new PathTerm(match.Groups["property"].Value.ToPascalCase(), PathType.Attribute));
            }
            // Otherwise, we check if the arrayIndex group was successful.
            else if (match.Groups["arrayIndex"].Success)
            {
                tokens.Add(new PathTerm(match.Groups["arrayIndex"].Value, PathType.ArrayIndex));
            }
        }
        return tokens;
    }

    private static object? EvaluatePath(object? current, List<PathTerm> tokens)
    {
        foreach (var token in tokens)
        {
            if (current == null)
            {
                return null;
            }

            if (current is RtTypeWithAttributes rtTypeWithAttributes)
            {
                if (rtTypeWithAttributes.Attributes.TryGetValue(token.Value, out var value))
                {
                    current = value;
                }
                else if (token.Value == nameof(RtEntity.RtId) ||
                         token.Value == nameof(RtEntity.RtWellKnownName) ||
                         token.Value == nameof(RtEntity.RtVersion) ||
                         token.Value == nameof(RtEntity.RtCreationDateTime) ||
                         token.Value == nameof(RtEntity.RtChangedDateTime))
                {
                    current = rtTypeWithAttributes.GetType().GetProperty(token.Value)?.GetValue(rtTypeWithAttributes);
                }
                else
                {
                    return null;
                }
            }
            else if (current is IEnumerable list && token.Type == PathType.ArrayIndex)
            {
                var indexStr = token.Value;
                if (indexStr == "*")
                {
                    var results = new List<object>();
                    foreach (var item in list)
                    {
                        var result = EvaluatePath(item, tokens.Skip(tokens.IndexOf(token) + 1).ToList());
                        if (result != null)
                        {
                            results.Add(result);
                        }
                    }

                    return results;
                }

                var enumerable = list as object[] ?? list.Cast<object>().ToArray();
                if (int.TryParse(indexStr, out int index) && index >= 0 && index < enumerable.Length)
                {
                    current = enumerable.ElementAt(index);
                }
                else if (index == -1)
                {
                    current = enumerable.LastOrDefault();
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        return current;
    }
}