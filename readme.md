
Octo Mesh is a powerful tool designed to seamlessly transform raw data into meaningful information, all while ensuring that the data is imbued with the context it needs to be truly insightful. Whether you're working with structured data, unstructured data, or anything in between, Octo Mesh empowers you to harness the full potential of your data.

At the heart of Octo Mesh lies the concept of Construction Kits. These kits serve as a fundamental building block for defining object models and providing the essential context that transforms data into actionable insights. With Octo Mesh, you can construct models that align with your specific needs, allowing you to shape data in ways that make sense for your organization.

# Key Features
## 1. Construction Kits 
Construction Kits are the cornerstone of Octo Mesh. They enable you to describe object models by specifying how data should be structured and organized. These kits act as blueprints, guiding the transformation process and ensuring that your data aligns with your goals and requirements.

## 2. Contextual Transformation
Octo Mesh goes beyond simple data manipulation; it's all about context. By using Construction Kits, you can provide the necessary context to your data, making it far more informative and actionable. This contextualization is key to deriving valuable insights from your data.

## 3. Validation
Ensuring data integrity is critical. Octo Mesh includes a robust Construction Kit Compiler that rigorously validates your defined models. This validation process helps catch errors early in the development phase, preventing costly mistakes downstream.

## 4. Dependency Resolution
Managing dependencies is a breeze with Octo Mesh. The built-in compiler resolves dependencies efficiently, simplifying the integration of your models into the transformation process.

## 5. Integration to C#
Using the package manager console, you can easily integrate your models into your C# project. This integration enables you to leverage your models in your code, allowing you to transform data programmatically.

## 6. Runtime Engine
This component of Octo Mesh is responsible for managing instances of data models defined by the Construction Kits using various repositories. It plays a key role in handling and storing data in different storage mediums. The basic implementation of this engine allows for data storage on a file or directory basis.

Moreover, there are advanced implementations of the Runtime Engine specifically designed for use with certain database systems like MongoDB. These implementations modify the base repository implementation to leverage the benefits of the respective database systems. This allows Octo Mesh users to adapt the storage and management of their data models flexibly to their specific needs and the technologies they use, ensuring efficient and effective data handling across different storage solutions. See the [Engine for MongoDB](https://github.com/meshmakers/octo-construction-kit-engine-mongodb) for more information.

# Overview of Libraries
## 1. Construction Kits
* **ConstructionKit.Contracts** - Contains the interfaces and contracts for defining Construction Kits and their associated models.
* **ConstructionKit.Engine** - The evaluation engine for Construction Kits. This library contains the compiler and the evaluator, which are responsible for compiling and evaluating Construction Kits, respectively.
* **ConstructionKit.Compiler** - The compiler for Construction Kits, that compiles to executable _octo-ckc_. It is responsible for compiling Construction Kits into executable models.
* **ConstructionKit.SourceGeneration** - The source generator for Construction Kits. It is responsible for generating C# source code from Construction Kits, to interact easier with the models.
* **ConstructionKit.Templates** - The project template for Construction Kits. It is responsible for generating a new Construction Kit project.
* **Runtime.Contracts** - Contains the interfaces and contracts for defining the Runtime Engine.
* **Runtime.Engine** - The Runtime Engine for Construction Kits. It is responsible for managing instances of data models defined by the Construction Kits using various repositories. This implementation is the base implementation of the Runtime Engine and allows for data storage on a file or directory basis.
* **SystemCkModel** - The base model (_System_) for Construction Kits. It is responsible for providing the base model for Construction Kits.

# Usage

## Create a new construction kit with a project template

## Use project template
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

# Create a new construction kit with the compiler

## Use the compiler
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

When using the project template, the construction kit gets compiled automatically when building the project. When using the compiler, the construction kit can be compiled with the following command:
```powershell
octo-ckc -c compile -p '<path of directory for construction kit>'
```

The compiler checks conformity of the construction kit files to the json schema files and consistency of the model to the dependencies and within the model. If the construction kit files are not valid, the compiler will throw an error.

In the ConstructionKit folder, a new file with the model id name gets created.

# Support and Feedback
If you encounter any issues or have questions while using Octo mesh, please don't hesitate to reach out to our support team at support@meshmakers.io. We value your feedback and are committed to helping you make the most of Octo Mesh.

# License
Octo Mesh is released under the MIT License. Feel free to use and modify it according to your needs, and we encourage contributions from the community to enhance the system further.

Thank you for choosing DTS as your data transformation solution. We look forward to seeing how it empowers you to turn data into valuable insights.

Happy transforming! 🚀