# Overview of Libraries
* **ConstructionKit.Contracts** - Contains the interfaces and contracts for defining Construction Kits and their associated models.
* **ConstructionKit.Engine** - The evaluation engine for Construction Kits. This library contains the compiler and the evaluator, which are responsible for compiling and evaluating Construction Kits, respectively.
* **ConstructionKit.Compiler** - The compiler for Construction Kits, that compiles to executable _octo-ckc_. It is responsible for compiling Construction Kits into executable models.
* **ConstructionKit.SourceGeneration** - The source generator for Construction Kits. It is responsible for generating C# source code from Construction Kits, to interact easier with the models.
* **ConstructionKit.Templates** - The project template for Construction Kits. It is responsible for generating a new Construction Kit project.
* **Runtime.Contracts** - Contains the interfaces and contracts for defining the Runtime Engine.
* **Runtime.Engine** - The Runtime Engine for Construction Kits. It is responsible for managing instances of data models defined by the Construction Kits using various repositories. This implementation is the base implementation of the Runtime Engine and allows for data storage on a file or directory basis.
* **SystemCkModel** - The base model (_System_) for Construction Kits. It is responsible for providing the base model for Construction Kits.

