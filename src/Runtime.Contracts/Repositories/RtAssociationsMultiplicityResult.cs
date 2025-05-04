namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Represents the result of an association multiplicity check.
/// </summary>
/// <param name="Pair">The pair of entity roles involved in the association.</param>
/// <param name="CurrentMultiplicity">The current multiplicity of the association.</param>
public record RtAssociationsMultiplicityResult(RtEntityRoleIdDirectionPair Pair, CurrentMultiplicity CurrentMultiplicity);