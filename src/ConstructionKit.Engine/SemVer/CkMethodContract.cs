using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     AB#6336 (F2.1 gate finding H2): the <b>invocation contract</b> of a CK method — the part a caller and a
///     redeclaring implementor depend on. One definition shared by error 122 (<c>CkInterfaceMethodConflict</c>,
///     <c>InheritanceResolver.InheritInterfaceMethods</c>) and the compatibility classifier (row I12), so the two cannot
///     drift.
///     <list type="bullet">
///         <item><c>kind</c> (Instance / Static);</item>
///         <item>
///             <c>parameters</c> as a set keyed by name (ordinal): <c>valueType</c>, <c>valueCkRecordId</c>,
///             <c>valueCkEnumId</c>, <c>isOptional</c>, <c>sensitive</c> (a redeclaration must not drop the redaction
///             of a value the interface declares sensitive);
///         </item>
///         <item><c>result</c>: present or absent, <c>valueType</c>, <c>valueCkRecordId</c>, <c>valueCkEnumId</c>;</item>
///         <item><c>errors</c>: the set of error codes (ordinal).</item>
///     </list>
///     Not part of the contract: descriptions, <c>authorization</c>, <c>execution</c>, visibility (rule 129) and the
///     order of parameters and errors. When a type redeclares an interface method, the type's own authorization and
///     execution apply.
/// </summary>
public static class CkMethodContract
{
    /// <summary>Method-level diff properties (<see cref="CkModelDiffService" />) that belong to the contract.</summary>
    public static IReadOnlyCollection<string> MethodProperties { get; } = ["kind", "result"];

    /// <summary>Parameter-level diff properties that belong to the contract (plus parameter added / removed).</summary>
    public static IReadOnlyCollection<string> ParameterProperties { get; } =
        ["valueType", "valueCkRecordId", "valueCkEnumId", "isOptional", "sensitive"];

    /// <summary>
    ///     True when the classified change touches the invocation contract of a method: a contract property of the
    ///     method or of a parameter, a parameter added or removed, an error code added or removed.
    /// </summary>
    public static bool IsContractChange(CkModelChange change) => change switch
    {
        { ElementKind: CkModelElementKind.TypeMethod or CkModelElementKind.InterfaceMethod,
            ChangeKind: CkModelChangeKind.Modified } => change.Property != null && MethodProperties.Contains(change.Property),
        { ElementKind: CkModelElementKind.MethodParameter, ChangeKind: CkModelChangeKind.Added or CkModelChangeKind.Removed } => true,
        { ElementKind: CkModelElementKind.MethodParameter, ChangeKind: CkModelChangeKind.Modified } =>
            change.Property != null && ParameterProperties.Contains(change.Property),
        { ElementKind: CkModelElementKind.MethodError, ChangeKind: CkModelChangeKind.Added or CkModelChangeKind.Removed } => true,
        _ => false
    };

    /// <summary>
    ///     The first contract field in which <paramref name="redeclared" /> differs from <paramref name="declared" />,
    ///     or null when both have the same invocation contract.
    /// </summary>
    public static string? FirstDifference(CkMethodDto declared, CkMethodDto redeclared)
    {
        if (declared.Kind != redeclared.Kind)
        {
            return $"kind '{declared.Kind}' vs '{redeclared.Kind}'";
        }

        var declaredParameters = (declared.Parameters ?? []).GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var redeclaredParameters = (redeclared.Parameters ?? []).GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var name in declaredParameters.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!redeclaredParameters.TryGetValue(name, out var other))
            {
                return $"parameter '{name}' missing";
            }

            var parameter = declaredParameters[name];
            if (parameter.ValueType != other.ValueType)
            {
                return $"parameter '{name}' valueType '{parameter.ValueType}' vs '{other.ValueType}'";
            }

            if (!Equals(parameter.ValueCkRecordId, other.ValueCkRecordId))
            {
                return $"parameter '{name}' valueCkRecordId '{parameter.ValueCkRecordId}' vs '{other.ValueCkRecordId}'";
            }

            if (!Equals(parameter.ValueCkEnumId, other.ValueCkEnumId))
            {
                return $"parameter '{name}' valueCkEnumId '{parameter.ValueCkEnumId}' vs '{other.ValueCkEnumId}'";
            }

            if (parameter.IsOptional != other.IsOptional)
            {
                return $"parameter '{name}' isOptional '{parameter.IsOptional}' vs '{other.IsOptional}'";
            }

            if (parameter.Sensitive != other.Sensitive)
            {
                return $"parameter '{name}' sensitive '{parameter.Sensitive}' vs '{other.Sensitive}'";
            }
        }

        var extra = redeclaredParameters.Keys.Except(declaredParameters.Keys, StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
        if (extra != null)
        {
            return $"additional parameter '{extra}'";
        }

        if ((declared.Result == null) != (redeclared.Result == null))
        {
            return declared.Result == null ? "result added" : "result missing";
        }

        if (declared.Result != null && redeclared.Result != null &&
            (declared.Result.ValueType != redeclared.Result.ValueType ||
             !Equals(declared.Result.ValueCkRecordId, redeclared.Result.ValueCkRecordId) ||
             !Equals(declared.Result.ValueCkEnumId, redeclared.Result.ValueCkEnumId)))
        {
            return "result type";
        }

        var declaredErrors = new HashSet<string>((declared.Errors ?? []).Select(e => e.Code), StringComparer.Ordinal);
        var redeclaredErrors = new HashSet<string>((redeclared.Errors ?? []).Select(e => e.Code), StringComparer.Ordinal);
        var missingError = declaredErrors.Except(redeclaredErrors, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal).FirstOrDefault();
        if (missingError != null)
        {
            return $"error code '{missingError}' missing";
        }

        var extraError = redeclaredErrors.Except(declaredErrors, StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal).FirstOrDefault();
        return extraError != null ? $"additional error code '{extraError}'" : null;
    }
}
