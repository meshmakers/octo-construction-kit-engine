//2024-04-05 REIMAR: THIS FILE EXISTS IN MULTIPLE REPOSITORIES. IF YOU CHANGE IT, MAKE SURE TO UPDATE ALL OF THEM.
//                   THIS FILE IS A TEMPORARY FIX FOR NETSTANDARD2_0 TO SUPPORT INIT ONLY SETTERS AND REQUIRED MEMBERS
//                   IT IS INJECTED INTO ALL PROJECTS IN THAT SOLUTION WITH Directory.Build.props


#if NETSTANDARD2_0

using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// To support init only setters
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public record IsExternalInit;
    

    /// <summary>
    /// To support required members
    /// </summary>
    public class RequiredMemberAttribute : Attribute { }
    
    /// <summary>
    /// To support required members
    /// </summary>
    public class CompilerFeatureRequiredAttribute : Attribute
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        public CompilerFeatureRequiredAttribute(string name) { }
    }
}
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// To support required setters
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public record SetsRequiredMembersAttribute;
}

#endif