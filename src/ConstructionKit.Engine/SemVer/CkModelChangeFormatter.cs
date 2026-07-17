using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Renders model changes as human readable one-liners, shared by the validation report and
///     the changelog generator.
/// </summary>
public static class CkModelChangeFormatter
{
    /// <summary>
    ///     Renders a change as a single line, e.g.
    ///     <c>Type 'Machine-1': isFinal changed 'false' → 'true'</c>.
    /// </summary>
    /// <param name="change">The change to render</param>
    /// <returns>The rendered line</returns>
    public static string Format(CkModelChange change)
    {
        var element = $"{GetElementKindLabel(change.ElementKind)} '{change.ElementId}'";
        switch (change.ChangeKind)
        {
            case CkModelChangeKind.Added:
                return change.NewValue == null ? $"{element} added" : $"{element} added ({change.NewValue})";
            case CkModelChangeKind.Removed:
                return change.OldValue == null ? $"{element} removed" : $"{element} removed ({change.OldValue})";
            default:
                return $"{element}: {change.Property} changed '{change.OldValue ?? "<none>"}' → '{change.NewValue ?? "<none>"}'";
        }
    }

    /// <summary>
    ///     Renders a classified change including its level and reasoning, e.g.
    ///     <c>MAJOR  Type 'Machine-1' removed — consumers reference the removed element</c>.
    /// </summary>
    /// <param name="classifiedChange">The classified change to render</param>
    /// <returns>The rendered line</returns>
    public static string Format(CkClassifiedModelChange classifiedChange)
    {
        return $"{GetLevelLabel(classifiedChange.Level),-5}  {Format(classifiedChange.Change)} — {classifiedChange.Reason}";
    }

    /// <summary>
    ///     Returns the display label of a semantic version level, e.g. <c>MAJOR</c>.
    /// </summary>
    /// <param name="level">The level</param>
    /// <returns>The display label</returns>
    public static string GetLevelLabel(CkSemVerLevel level)
    {
        return level.ToString().ToUpperInvariant();
    }

    private static string GetElementKindLabel(CkModelElementKind elementKind)
    {
        return elementKind switch
        {
            CkModelElementKind.Model => "Model",
            CkModelElementKind.Dependency => "Dependency",
            CkModelElementKind.Type => "Type",
            CkModelElementKind.TypeAttribute => "Type attribute",
            CkModelElementKind.TypeAssociation => "Type association",
            CkModelElementKind.TypeIndex => "Index",
            CkModelElementKind.Attribute => "Attribute",
            CkModelElementKind.Enum => "Enum",
            CkModelElementKind.EnumValue => "Enum value",
            CkModelElementKind.Record => "Record",
            CkModelElementKind.RecordAttribute => "Record attribute",
            CkModelElementKind.AssociationRole => "Association role",
            CkModelElementKind.AssociationRoleAttribute => "Association role attribute",
            _ => elementKind.ToString()
        };
    }
}
