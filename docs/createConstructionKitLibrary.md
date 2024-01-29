# Create a new construction kit
There are two ways to create a new construction kit, using a .NET project template or using the compiler.

## Create with project template
New construction kits can be created using the project template. The template can be installed from NuGet:

```powershell
dotnet new install Meshmakers.Octo.ConstructionKit.Templates
```

To install a specific version of the template, use the following command:

```powershell
dotnet new install Meshmakers.Octo.ConstructionKit.Templates::0.0.2312.12001
```

Create a new project with your favorite development environment, the template should be available as `Octo Mesh Construction Kit Library`, alternatively you can use the following command:

```powershell
dotnet new ConstructionKit -n <name of project>
```

Build the project, a new construction kit gets created within the folder `ConstructionKit` in the project root.

## Create with the compiler

### Install the compiler
The compiler can be installed from NuGet:
```powershell
dotnet tool install meshmakers.Octo.ConstructionKit.Compiler --global # global available
dotnet tool install meshmakers.Octo.ConstructionKit.Compiler --local --create-manifest-if-needed # local available
```

You can also install a specific version of the compiler:
```powershell
dotnet tool install meshmakers.Octo.ConstructionKit.Compiler --global --version 0.0.2312.12001 # global available
dotnet tool install meshmakers.Octo.ConstructionKit.Compiler --local --version 0.0.2312.12001 --create-manifest-if-needed # local available
```

**Globally the tool gets available as `octo-ckc`, locally as `dotnet octo-ckc`.**

Create a new construction kit with the following command:
```powershell
octo-ckc -c new -p '<path of directory for construction kit>'
```

# Edit a construction kit
Construction kits can be edited with your favorite development environment. The construction kit files are located in the `ConstructionKit` folder in the project root. Files are typically described in yaml format. The following elements are available:
* CkModel: Describes the id of the model and dependencies to other models.
* CkEnums: Describes the enums of the model (id, descriptions, values, etc.)
* CkRecords: Describes the records of the model (id, descriptions, assigned attributes, etc.)
* CkAttributes: Describes the attributes of the model (id, descriptions, data types, etc.)
* CkAssociations: Describes the associations of the model (id, descriptions, cardinality, etc.)
* CkTypes: Describes the types of the model (id, descriptions, assigned attributes and associations, etc.)

There are json schema files available for the construction kit files. These files can be used to validate the construction kit files. The schema files are available in the [ConstructionKit.Contracts](https://github.com/meshmakers/octo-construction-kit-engine/tree/main/src/ConstructionKit.Contracts/Serialization/Schema) project.

It is recommended to split construction kits into multiple files. It is a good practise to separate attributes/associations for specific use-cases in multiple files and to use for each enum/record/type a separate file.

# Compile a construction kit

When using the project template, the construction kit gets compiled automatically when building the project. 

When using the compiler, the construction kit can be compiled with the following command:
```powershell
octo-ckc -c compile -p '<path of directory for construction kit>'
```

The compiler checks conformity of the construction kit files to the json schema files and consistency of the model to the dependencies and within the model. If the construction kit files are not valid, the compiler will throw an error.

In the ConstructionKit folder, a new file with the model id name gets created.


# Import to Octo Mesh
Octo Mesh can import construction kits. The import can be done with the octo-cli tool. octo-cli is a command line utility to manage Octo Mesh as a admin.

To install the tool, use [chocolatey](https://community.chocolatey.org/) or [Windows Package Manager](https://learn.microsoft.com/de-DE/windows/package-manager/) with the following command:
```powershell
winget install -e --id meshmakers.octo-cli
# or
choco install octo-cli
```

Depending on the installation method of OctoMesh, the tool needs to be configured to use the correct endpoints. The following command configures the tool to use the default endpoints of OctoMesh:
```powershell
octo-cli -c Config -asu "https://localhost:5001/" -isu "https://localhost:5003/" -bsu "https://localhost:5009/" -tid "meshtest"
```
Hint: The parameter -tid defines the used tenant.

Next, the tool needs to be logged in to OctoMesh. The following command logs in to OctoMesh using an administrator account:
```powershell
octo-cli -c Login -i
```

Optional: Create a new tenant in OctoMesh:
```powershell
octo-cli -c Create -tid meshtest -db meshtest
```

Hint: To delete tenants, use the following command:
```powershell
octo-cli -c delete -tid meshtest
```

After the tenant is created, the API of Asset Repository can be used to access data of the tenant.
https://localhost:5001/tenants/meshtest/graphql/playground

To import a construction kit, use the following command:
```powershell
octo-cli -c importck -f ./ck-sample2.yaml -w
```
Hint: The parameter -f defines the path to the construction kit file that has been created by the octo-ckc compiler.

