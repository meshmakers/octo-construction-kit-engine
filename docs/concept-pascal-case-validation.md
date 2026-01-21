# Concept: Pascal Case Validation for Construction Kit Compiler

## Overview

This document outlines the concept for extending the Construction Kit Compiler with stronger syntax validation. The goal is to enforce Pascal Case naming conventions for IDs and names across all CK model elements.

## Current State

### Existing Validation in JSON Schemas

The JSON Schema files already contain `pattern` fields for basic character validation:

| Schema File | Field | Current Pattern |
|-------------|-------|-----------------|
| `construction-kit-elements-type.schema.json` | `typeId` | `^[a-zA-Z0-9-_.]+$` |
| `construction-kit-elements-record.schema.json` | `recordId` | `^[a-zA-Z0-9-_.]+$` |
| `construction-kit-elements-attribute.schema.json` | `id` (attributeId) | `^[a-zA-Z0-9-_.]+$` |
| `construction-kit-elements-attribute.schema.json` | `name` | `^[a-zA-Z0-9_]+$` |
| `construction-kit-elements-enum.schema.json` | `enumId` | `^[a-zA-Z0-9-_.]+$` |
| `construction-kit-elements-enum.schema.json` | `name` (value) | `^[a-zA-Z0-9-_.]+$` |
| `construction-kit-elements-associationRole.schema.json` | `id` | `^[a-zA-Z0-9-_.]+$` |
| `construction-kit-elements-associationRole.schema.json` | `inboundName` | `^[a-zA-Z0-9_]+$` |
| `construction-kit-elements-associationRole.schema.json` | `outboundName` | `^[a-zA-Z0-9_]+$` |

**Problem**: These patterns only ensure allowed characters but do not enforce Pascal Case.

### Additional Validation in ElementResolver

The `ElementResolver.cs` performs runtime validation using `CompilerStatics.AllowedCharactersInNamesRegex`, which duplicates the schema validation.

## Requirements

### Elements Requiring Pascal Case Validation

| Element | Property | Example (Valid) | Example (Invalid) |
|---------|----------|-----------------|-------------------|
| Type | `typeId` | `Entity`, `ApiResource` | `entity`, `api_resource` |
| Record | `recordId` | `FieldFilter`, `UserClaim` | `fieldFilter`, `user_claim` |
| Attribute | `id` (attributeId) | `AttributePath`, `Name` | `attributePath`, `name` |
| Attribute | `name` | `AttributePath`, `Operator` | `attributePath`, `operator` |
| Enum | `enumId` | `FieldFilterOperator`, `TokenType` | `fieldFilterOperator`, `token_type` |
| Enum Value | `name` | `Equals`, `NotEquals`, `LessThan` | `equals`, `NOT_EQUALS` |
| Association Role | `id` | `ParentChild`, `Related` | `parentChild`, `parent_child` |
| Association Role | `inboundName` | `Children`, `RelatesFrom` | `children`, `relates_from` |
| Association Role | `outboundName` | `Parent`, `RelatesTo` | `parent`, `relates_to` |

### Pascal Case Definition

Pascal Case (also known as Upper Camel Case) follows these rules:
1. First character is uppercase (A-Z)
2. Subsequent characters are alphanumeric (a-z, A-Z, 0-9)
3. No underscores, hyphens, or dots between words

**Regex Pattern**: `^[A-Z][a-zA-Z0-9]*$`

## Technical Design

### Primary Approach: JSON Schema Validation

Update the `pattern` fields in all JSON Schema files to enforce Pascal Case.

#### Advantages of Schema-Based Validation

1. **Early Detection**: Errors are caught during YAML parsing, before compilation
2. **IDE Support**: Editors like VS Code show errors inline with schema validation
3. **Single Source of Truth**: Schema defines the contract, no code duplication
4. **Better Error Messages**: Schema validators provide detailed path information
5. **Documentation**: Schema serves as living documentation

### Schema Changes

#### 1. Simple IDs (typeId, recordId, enumId, attributeId, association id)

**Current**:
```json
"pattern": "^[a-zA-Z0-9-_.]+$"
```

**New**:
```json
"pattern": "^[A-Z][a-zA-Z0-9]*$"
```

#### 2. Names (attribute name, enum value name, inboundName, outboundName)

**Current**:
```json
"pattern": "^[a-zA-Z0-9_]+$"
```

**New**:
```json
"pattern": "^[A-Z][a-zA-Z0-9]*$"
```

#### 3. Qualified IDs (with variable prefix like `${this}/`)

For fields like `derivedFromCkTypeId`, `id` in type attributes, `targetCkTypeId`, etc.

**Current**:
```json
"pattern": "^(?:[a-zA-Z0-9-_.]+|\\$\\{[a-zA-Z0-9_.]+\\})/[a-zA-Z0-9-_.]+$"
```

**New**:
```json
"pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
```

This pattern:
- Allows model prefix as Pascal Case OR variable like `${this}`
- Requires the element name (after `/`) to be Pascal Case

### Files to Modify

#### JSON Schema Files

| File | Fields to Update |
|------|------------------|
| `construction-kit-elements-type.schema.json` | `typeId`, `derivedFromCkTypeId`, association `id`, `targetCkTypeId`, `targetAttributes` |
| `construction-kit-elements-record.schema.json` | `recordId`, `derivedFromCkRecordId` |
| `construction-kit-elements-attribute.schema.json` | `id` (CkAttribute), `id` (CkTypeAttribute), `name`, `valueCkRecordId`, `valueCkEnumId` |
| `construction-kit-elements-enum.schema.json` | `enumId`, value `name` |
| `construction-kit-elements-associationRole.schema.json` | `id`, `inboundName`, `outboundName` |
| `construction-kit-compiled.schema.json` | Same fields as above (for compiled output) |

### Detailed Schema Changes

#### construction-kit-elements-type.schema.json

```json
{
  "definitions": {
    "CkType": {
      "properties": {
        "typeId": {
          "type": "string",
          "pattern": "^[A-Z][a-zA-Z0-9]*$",
          "description": "Type identifier in Pascal Case (e.g., 'Entity', 'ApiResource')"
        },
        "derivedFromCkTypeId": {
          "type": "string",
          "pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
        },
        "associations": {
          "items": {
            "properties": {
              "id": {
                "type": "string",
                "pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
              },
              "targetCkTypeId": {
                "type": "string",
                "pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
              },
              "targetAttributes": {
                "items": {
                  "type": "string",
                  "pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
                }
              }
            }
          }
        }
      }
    }
  }
}
```

#### construction-kit-elements-record.schema.json

```json
{
  "definitions": {
    "CkRecord": {
      "properties": {
        "recordId": {
          "type": "string",
          "pattern": "^[A-Z][a-zA-Z0-9]*$",
          "description": "Record identifier in Pascal Case (e.g., 'FieldFilter', 'UserClaim')"
        },
        "derivedFromCkRecordId": {
          "type": "string",
          "pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
        }
      }
    }
  }
}
```

#### construction-kit-elements-attribute.schema.json

```json
{
  "definitions": {
    "CkTypeAttribute": {
      "properties": {
        "id": {
          "type": "string",
          "pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
        },
        "name": {
          "type": "string",
          "pattern": "^[A-Z][a-zA-Z0-9]*$",
          "description": "Attribute name in Pascal Case (e.g., 'AttributePath', 'Name')"
        }
      }
    },
    "CkAttribute": {
      "properties": {
        "id": {
          "type": "string",
          "pattern": "^[A-Z][a-zA-Z0-9]*$",
          "description": "Attribute identifier in Pascal Case"
        },
        "valueCkRecordId": {
          "type": "string",
          "pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
        },
        "valueCkEnumId": {
          "type": "string",
          "pattern": "^(?:[A-Z][a-zA-Z0-9]*|\\$\\{[a-zA-Z0-9_.]+\\})/[A-Z][a-zA-Z0-9]*$"
        }
      }
    }
  }
}
```

#### construction-kit-elements-enum.schema.json

```json
{
  "definitions": {
    "CkEnum": {
      "properties": {
        "enumId": {
          "type": "string",
          "pattern": "^[A-Z][a-zA-Z0-9]*$",
          "description": "Enum identifier in Pascal Case (e.g., 'FieldFilterOperator')"
        },
        "values": {
          "items": {
            "properties": {
              "name": {
                "type": "string",
                "pattern": "^[A-Z][a-zA-Z0-9]*$",
                "description": "Enum value name in Pascal Case (e.g., 'Equals', 'NotEquals')"
              }
            }
          }
        }
      }
    }
  }
}
```

#### construction-kit-elements-associationRole.schema.json

```json
{
  "definitions": {
    "CkAssociationRole": {
      "properties": {
        "id": {
          "type": "string",
          "pattern": "^[A-Z][a-zA-Z0-9]*$",
          "description": "Association role identifier in Pascal Case (e.g., 'ParentChild')"
        },
        "inboundName": {
          "type": "string",
          "pattern": "^[A-Z][a-zA-Z0-9]*$",
          "description": "Inbound navigation name in Pascal Case (e.g., 'Children')"
        },
        "outboundName": {
          "type": "string",
          "pattern": "^[A-Z][a-zA-Z0-9]*$",
          "description": "Outbound navigation name in Pascal Case (e.g., 'Parent')"
        }
      }
    }
  }
}
```

### Secondary: Update CompilerStatics.cs

For consistency, update the regex constants in `CompilerStatics.cs`:

```csharp
/// <summary>
/// Regex pattern for Pascal Case validation.
/// Matches identifiers starting with uppercase letter, followed by alphanumeric characters.
/// </summary>
public const string PascalCaseRegex = @"^[A-Z][a-zA-Z0-9]*$";

/// <summary>
/// Regex pattern for qualified identifiers with Pascal Case.
/// Supports variable prefixes like ${this}/ followed by Pascal Case identifier.
/// </summary>
public const string QualifiedPascalCaseRegex =
    @"^(?:[A-Z][a-zA-Z0-9]*|\$\{[a-zA-Z0-9_.]+\})/[A-Z][a-zA-Z0-9]*$";

// Keep existing patterns for backward compatibility during migration
[Obsolete("Use PascalCaseRegex instead")]
public const string AllowedCharactersInNamesRegex = @"^[a-zA-Z0-9_.]+$";
```

### Optional: Add Helpful Error Messages

Update `MessageCodes.cs` to provide guidance when Pascal Case validation fails:

```csharp
internal static OperationMessage CkTypeIdNotPascalCase(string? location, object ckTypeId) =>
    GetMessage("CkTypeIdNotPascalCase", location, ckTypeId);

// In Templates dictionary:
{
    "CkTypeIdNotPascalCase",
    new OperationMessageTemplate(MessageLevel.Error,
        63, "CkTypeId '{ckTypeId}' must be in Pascal Case. " +
            "Pascal Case starts with an uppercase letter (A-Z) followed by alphanumeric characters. " +
            "Examples: 'Entity', 'ApiResource', 'UserProfile'. " +
            "Invalid: 'entity', 'apiResource', 'user_profile'.",
        new [] {"ckTypeId"})
}
```

## Implementation Steps

### Phase 1: Schema Updates (Primary)

1. Update `construction-kit-elements-type.schema.json`
2. Update `construction-kit-elements-record.schema.json`
3. Update `construction-kit-elements-attribute.schema.json`
4. Update `construction-kit-elements-enum.schema.json`
5. Update `construction-kit-elements-associationRole.schema.json`
6. Update `construction-kit-compiled.schema.json` (for compiled output validation)

### Phase 2: Update Constants

1. Add `PascalCaseRegex` to `CompilerStatics.cs`
2. Add `QualifiedPascalCaseRegex` to `CompilerStatics.cs`
3. Mark old constants as obsolete

### Phase 3: Update ElementResolver (Optional)

The `ElementResolver.cs` validation becomes redundant after schema changes but can be kept for:
- More specific error messages
- Additional context in error reporting
- Fallback if schema validation is bypassed

### Phase 4: Fix Existing Models

Update existing Construction Kit models to comply with Pascal Case:

**System Model** (`src/SystemCkModel/ConstructionKit/`):
- All IDs and names already appear to follow Pascal Case (verified)

**Test Models** (`tests/TestCkModel/`):
- Review and update any non-compliant definitions

### Phase 5: Testing

1. **Schema Tests**: Validate patterns match expected inputs
2. **Integration Tests**: Compile models with valid/invalid names
3. **Regression Tests**: Ensure existing valid models still compile

## Test Cases

### Valid Names (Should Pass)

```
Entity, ApiResource, FieldFilter, ParentChild, Equals, NotEquals,
RtBlueprintSource, Version2, OAuth2Client, XMLParser, HTTPRequest,
ID, URL, API, Children, Parent, RelatesTo, RelatesFrom
```

### Invalid Names (Should Fail)

```
entity          → starts with lowercase
apiResource     → starts with lowercase (camelCase)
field_filter    → contains underscore (snake_case)
FIELD_FILTER    → not Pascal Case (SCREAMING_SNAKE_CASE)
parent-child    → contains hyphen (kebab-case)
123Type         → starts with number
_private        → starts with underscore
```

## Migration Strategy

### For Existing Models

1. **Audit**: Run schema validation on all existing models
2. **Report**: Generate list of non-compliant identifiers
3. **Update**: Fix identifiers in YAML files
4. **Verify**: Recompile all models

### Backward Compatibility Options

If immediate enforcement is not possible:

**Option A: Staged Rollout**
1. Release schema with Pascal Case as `warning` (using custom validation)
2. After migration period, enforce as `error`

**Option B: Configuration Flag**
Add compiler option to toggle strict naming:
```yaml
# ck-meta.yaml
compilerOptions:
  enforceNamingConventions: true  # default: true for new models
```

## Summary of Changes

| File | Change Type | Description |
|------|-------------|-------------|
| `construction-kit-elements-type.schema.json` | Modify | Update patterns for `typeId`, qualified IDs |
| `construction-kit-elements-record.schema.json` | Modify | Update patterns for `recordId`, qualified IDs |
| `construction-kit-elements-attribute.schema.json` | Modify | Update patterns for `id`, `name`, qualified IDs |
| `construction-kit-elements-enum.schema.json` | Modify | Update patterns for `enumId`, value `name` |
| `construction-kit-elements-associationRole.schema.json` | Modify | Update patterns for `id`, `inboundName`, `outboundName` |
| `construction-kit-compiled.schema.json` | Modify | Mirror changes for compiled output |
| `CompilerStatics.cs` | Modify | Add Pascal Case regex constants |
| `MessageCodes.cs` | Modify | Add helpful error messages (optional) |

## Conclusion

Using JSON Schema validation as the primary mechanism for Pascal Case enforcement provides:
- **Early feedback** during development (IDE integration)
- **Consistent validation** across all tools that use the schema
- **Self-documenting** constraints
- **Minimal code changes** in the compiler itself

The schema-based approach is the recommended solution, with optional enhancements in `ElementResolver` for improved error messaging.
