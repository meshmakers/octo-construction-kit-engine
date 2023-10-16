using System.Linq.Expressions;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

internal static class ExpressionCombiner
{
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> exp, Expression<Func<T, bool>> newExp)
    { 
        // get the visitor
        var visitor = new ParameterUpdateVisitor(newExp.Parameters.First(), exp.Parameters.First());
        // replace the parameter in the expression just created
        var visitedExp = visitor.Visit(newExp) as Expression<Func<T, bool>>;
        if (visitedExp == null)
        {
            throw new NotSupportedException();
        }
        
        // now you can and together the two expressions
        var binExp = Expression.And(exp.Body, visitedExp.Body);
        // and return a new lambda, that will do what you want. NOTE that the binExp has reference only to te newExp.Parameters[0] (there is only 1) parameter, and no other
        return Expression.Lambda<Func<T, bool>>(binExp, exp.Parameters);
    }
    
    public static Expression<Func<T, bool>> AndAlso<T>(this Expression<Func<T, bool>> exp, Expression<Func<T, bool>> newExp)
    {
        var visitor = new ParameterUpdateVisitor(newExp.Parameters.First(), exp.Parameters.First());
        var body2WithParam1 = visitor.Visit(newExp.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(exp.Body, body2WithParam1), exp.Parameters);
    }

    public static Expression<Func<T, bool>> OrElse<T>(this Expression<Func<T, bool>> exp, Expression<Func<T, bool>> newExp)
    {
        var visitor = new ParameterUpdateVisitor(newExp.Parameters.First(), exp.Parameters.First());
        var body2WithParam1 = visitor.Visit(newExp.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(exp.Body, body2WithParam1), exp.Parameters);
    }
         
    class ParameterUpdateVisitor : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParameter;
        private readonly ParameterExpression _newParameter;

        public ParameterUpdateVisitor(ParameterExpression oldParameter, ParameterExpression newParameter)
        {
            _oldParameter = oldParameter;
            _newParameter = newParameter;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (object.ReferenceEquals(node, _oldParameter))
                return _newParameter;

            return base.VisitParameter(node);
        }
    }
}