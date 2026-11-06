using Meshmakers.Octo.Runtime.Contracts.Formulas;

namespace Meshmakers.Octo.Runtime.Engine.Formulas;

/// <summary>
/// All prepared variants of one formula on one thread (AB#8331): one <see cref="PreparedFormula"/> per set of
/// identifiers the supplied values cover. Rows of one archive normally supply the same set, so a backfill
/// uses a single variant.
/// </summary>
internal sealed class FormulaShape
{
    /// <summary>Formulas with more identifiers are evaluated without caching variants.</summary>
    internal const int MaxIdentifiers = 64;

    /// <summary>
    /// Evaluations after which a variant is parsed again: bounds the growth of mXparser's compiled elements
    /// for formulas with <c>if</c> (see <see cref="PreparedFormula.Evaluations"/>) to ~1.6 MB per variant while
    /// the parse (~200 KiB) costs ~50 bytes per evaluation.
    /// </summary>
    internal const int MaxEvaluationsPerParse = 4096;

    /// <summary>Upper bound of variants kept per formula; rows that keep changing their columns only cost speed.</summary>
    internal const int MaxVariants = 32;

    private readonly string _expression;
    private readonly string _normalized;
    private readonly string[] _identifiers;
    private readonly Dictionary<ulong, PreparedFormula> _variants = new();

    internal FormulaShape(string expression, string normalized)
    {
        _expression = expression;
        _normalized = normalized;
        _identifiers = PreparedFormula.Identifiers(normalized);
    }

    /// <summary>Evaluates with the variant for the identifiers <paramref name="values"/> supplies.</summary>
    internal double Evaluate(IReadOnlyDictionary<string, double> values)
    {
        if (_identifiers.Length > MaxIdentifiers)
        {
            var bound = _identifiers.Where(values.ContainsKey).ToArray();
            return PreparedFormula.Create(_expression, _normalized, bound).Evaluate(values);
        }

        ulong mask = 0;
        for (var i = 0; i < _identifiers.Length; i++)
        {
            if (values.ContainsKey(_identifiers[i]))
            {
                mask |= 1UL << i;
            }
        }

        if (!_variants.TryGetValue(mask, out var variant) || variant.Evaluations >= MaxEvaluationsPerParse)
        {
            var bound = new List<string>();
            for (var i = 0; i < _identifiers.Length; i++)
            {
                if ((mask & (1UL << i)) != 0)
                {
                    bound.Add(_identifiers[i]);
                }
            }

            if (variant is null && _variants.Count >= MaxVariants)
            {
                _variants.Clear();
            }

            variant = PreparedFormula.Create(_expression, _normalized, bound);
            _variants[mask] = variant;
        }

        try
        {
            return variant.Evaluate(values);
        }
        catch (FormulaEvaluationException)
        {
            // An aborted evaluation may leave the mXparser expression in an undefined state: drop it.
            _variants.Remove(mask);
            throw;
        }
    }
}
