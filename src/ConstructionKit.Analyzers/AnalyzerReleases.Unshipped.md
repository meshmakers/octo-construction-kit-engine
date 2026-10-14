; Unshipped analyzer changes
; https://github.com/dotnet/roslyn-analyzers/blob/master/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

| Rule ID | Category         | Severity | Notes                                                                       |
|---------|------------------|----------|-----------------------------------------------------------------------------|
| OM1005  | Construction Kit | Info     | Generated versioned CK id (CkIds.Ck<Element>Id) used at run time (AB#6673); raise to error per project |
