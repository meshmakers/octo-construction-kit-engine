# Construction Kit Models

## Overview
Construction Kit (CK) models are the core data modeling framework in Octo Mesh. They are defined in YAML files and follow a specific schema.

## Model Structure

### Model Metadata (ckModel.yaml)
Every CK model has a root `ckModel.yaml` file containing:
- `$schema`: Reference to JSON schema (https://schemas.meshmakers.cloud/construction-kit-meta.schema.json)
- `modelId`: Unique identifier in format "{Name}-{Version}" (e.g., "System-1.0.0")
- `dependencies`: Optional array of dependent model IDs

### Model Elements
Models are organized in subdirectories:

#### 1. Types (/types)
Define entity types and domain objects. Examples from SystemCkModel:
- `entity.yaml` - Base entity type
- `tenant.yaml` - Multi-tenancy support
- `configuration.yaml` - Configuration management
- `query.yaml` - Query definitions
- `autoIncrement.yaml` - Auto-increment functionality

#### 2. Attributes (/attributes)
Define reusable attributes that can be applied to types. Examples:
- `base.yaml` - Base attributes
- `tenant.yaml` - Tenant-related attributes
- `query.yaml` - Query attributes
- `configuration.yaml` - Configuration attributes
- `autoIncrement.yaml` - Auto-increment attributes

#### 3. Enums (/enums)
Define enumeration types. Examples:
- `queryTypes.yaml` - Types of queries
- `environmentModes.yaml` - Environment modes
- `fieldFilterOperators.yaml` - Filter operators
- `sortOrders.yaml` - Sort order options
- `maintenanceLevels.yaml` - Maintenance level options

#### 4. Records (/records)
Define record types (data transfer objects). Examples:
- `fieldFilter.yaml` - Field filter definitions
- `sortOrderItem.yaml` - Sort order items
- `attributeSearchFilter.yaml` - Attribute search filters
- `textSearchFilter.yaml` - Text search filters

#### 5. Associations (/associations)
Define relationships between types:
- `ck-associations.yaml` - Association definitions

## Key C# Types for CK Models

### Identifiers
- `CkId`: Base identifier for CK elements
- `CkModelId`: Model identifier (e.g., "System-1.0.0")
- `CkTypeId`: Type identifier
- `CkAttributeId`: Attribute identifier
- `CkEnumId`: Enum identifier
- `CkRecordId`: Record identifier
- `CkAssociationRoleId`: Association role identifier

### Version Management
- `CkVersion`: Version representation (semantic versioning)
- `CkVersionRange`: Version range specification
- `CkModelIdVersionRange`: Model ID with version range

### DTOs (Data Transfer Objects)
Located in `ConstructionKit.Contracts/DataTransferObjects/`:
- `CkModelRootBase`: Base for model roots
- `CkCompiledModelRoot`: Compiled model representation
- `CkMetaRootDto`: Model metadata
- `CkTypeDto`: Type definitions
- `CkAttributeDto`: Attribute definitions
- `CkEnumDto`: Enum definitions
- `CkRecordDto`: Record definitions
- `CkAssociationRoleDto`: Association role definitions

### Dependency Graph
Graph-based representation of model elements (in `DependencyGraph/`):
- `ICkModelGraph`: Model graph interface
- `CkModelGraph`: Model graph implementation
- `CkTypeGraph`: Type dependency graph
- `CkAttributeGraph`: Attribute dependency graph
- `CkEnumGraph`: Enum dependency graph
- `CkRecordGraph`: Record dependency graph
- `CkAssociationRoleGraph`: Association role dependency graph

## Compilation Process
1. **Parse**: YAML files are parsed using `CkYamlSerializer`
2. **Validate**: Validated against JSON schema using `CkSchemaValidator`
3. **Resolve**: Dependencies and references resolved using resolvers
4. **Compile**: Generate compiled model with `CompilerService`
5. **Cache**: Store in cache using `CkCacheService`
6. **Generate**: Optionally generate source code or documentation

## MSBuild Integration
Key properties in Directory.Build.props:
- `OctoCompileCkModel`: Controls CK model compilation (default: true)
- `OctoPublishCkModel`: Controls CK model publishing (default: false)
- `OctoGenerateCkModelServiceClass`: Generate service classes (default: true)
- `OctoGenerateCkDocumentation`: Generate documentation (default: false)