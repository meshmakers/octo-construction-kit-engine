using FakeItEasy;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

public class RepositoryDependencyResolverTests
{
    private readonly IRepositoryManagementService _repositoryManagementService;
    private readonly IVariableResolver _variableResolver;
    private readonly IOriginFileResolver _originFileResolver;
    private readonly RepositoryDependencyResolver _resolver;
    private readonly CkModelGraph _modelGraph;
    private readonly OperationResult _operationResult;

    public RepositoryDependencyResolverTests(ITestOutputHelper output)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddXUnit(output); // Redirect logs to xUnit test output
        });

        _variableResolver = A.Fake<IVariableResolver>();
        _repositoryManagementService = A.Fake<IRepositoryManagementService>();
        _originFileResolver = A.Fake<IOriginFileResolver>();
        var repositoryManagerLazy = new Lazy<IRepositoryManagementService>(() => _repositoryManagementService);

        _resolver = new RepositoryDependencyResolver(loggerFactory.CreateLogger<RepositoryDependencyResolver>(), repositoryManagerLazy);
        _modelGraph = new CkModelGraph();
        _operationResult = new OperationResult();
    }

    [Fact]
    public async Task HardResolveDependenciesAsync_WithVersionRanges_ReturnsResolvedDependencies()
    {
        // Arrange
        var dependencyVersionRange = new CkModelIdVersionRange("TestModel-1.0.0");
        var dependencyVersionRanges = new List<CkModelIdVersionRange> { dependencyVersionRange };
        var expectedModelId = new CkModelId("TestModel-1.0.0");
        var expectedModel = CreateTestCkModel(expectedModelId);

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(dependencyVersionRange, null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = expectedModelId });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(expectedModelId, _operationResult, null, null))
            .Returns(expectedModel);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.HardResolveDependenciesAsync(
            dependencyVersionRanges, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.Single(result);
        Assert.Contains(expectedModelId, result);
        Assert.Empty(_operationResult.Messages);
        A.CallTo(() => _variableResolver.SetVariable(expectedModelId.Name, expectedModelId.FullName))
            .MustHaveHappenedOnceExactly();
    }

    [Fact]
    public async Task HardResolveDependenciesAsync_WithCkModelIds_ReturnsResolvedDependencies()
    {
        // Arrange
        var dependencyModelId = new CkModelId("TestModel-1.0.0");
        var dependencies = new List<CkModelId> { dependencyModelId };
        var expectedModel = CreateTestCkModel(dependencyModelId);

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(A<CkModelIdVersionRange>._, null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = dependencyModelId });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(dependencyModelId, _operationResult, null, null))
            .Returns(expectedModel);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.HardResolveDependenciesAsync(
            dependencies, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.Single(result);
        Assert.Contains(dependencyModelId, result);
        Assert.Empty(_operationResult.Messages);
    }

    [Fact]
    public async Task HardResolveDependenciesAsync_WithUnresolvableDependency_AddsErrorMessageAndThrows()
    {
        // Arrange
        var dependencyVersionRange = new CkModelIdVersionRange("NonExistentModel-1.0.0");
        var dependencyVersionRanges = new List<CkModelIdVersionRange> { dependencyVersionRange };

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(dependencyVersionRange, null))
            .Returns(new ModelExistingResult { Exists = false, ModelId = null });

        A.CallTo(() => _originFileResolver.Resolve(dependencyVersionRange))
            .Returns("test-file.yaml");

        // Act
        await Assert.ThrowsAsync<ModelValidationException>(async ()=> await _resolver.HardResolveDependenciesAsync(
            dependencyVersionRanges, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult));

        // Assert
        Assert.Single(_operationResult.Messages);
        Assert.Equal(1, _operationResult.Messages.First().MessageNumber);
    }

    [Fact]
    public async Task SoftResolveDependenciesAsync_ReturnsCompleteResolveResult()
    {
        // Arrange
        var dependencyModelId = new CkModelId("TestModel-1.0.0");
        var dependencies = new List<CkModelId> { dependencyModelId };
        var expectedModel = CreateTestCkModel(dependencyModelId);

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(A<CkModelIdVersionRange>._, null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = dependencyModelId });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(dependencyModelId, _operationResult, null, null))
            .Returns(expectedModel);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.SoftResolveDependenciesAsync(
            dependencies, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.RootDependencyModelIds);
        Assert.Contains(dependencyModelId, result.RootDependencyModelIds);
        Assert.Empty(result.UnresolvedDependencyModelIds);
        Assert.Empty(result.SkippedModelIds);
    }

    [Fact]
    public async Task SoftResolveDependenciesAsync_IndirectReferences_OK()
    {
        // Arrange
        var ckModelIdA = new CkModelId("A-1.0.0");
        var ckModelIdB = new CkModelId("B-1.0.0");
        var ckModelIdC = new CkModelId("C-1.0.0");
        var dependenciesA = new List<CkModelId> { ckModelIdB };
        var dependenciesB = new List<CkModelId> { ckModelIdC };
        var expectedModelA = CreateTestCkModel(ckModelIdA, dependenciesA.ToArray());
        var expectedModelB = CreateTestCkModel(ckModelIdB, dependenciesB.ToArray());
        var expectedModelC = CreateTestCkModel(ckModelIdC);

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdA.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdA });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdB.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdB });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdC.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdC });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdA, _operationResult, null, null))
            .Returns(expectedModelA);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdB, _operationResult, null, null))
            .Returns(expectedModelB);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdC, _operationResult, null, null))
            .Returns(expectedModelC);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.SoftResolveDependenciesAsync(
            new List<CkModelId> { ckModelIdA }, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.RootDependencyModelIds);
        Assert.Contains(ckModelIdA, result.RootDependencyModelIds);
        Assert.Empty(result.UnresolvedDependencyModelIds);
        Assert.Empty(result.SkippedModelIds);
    }

    [Fact]
    public async Task SoftResolveDependenciesAsync_IndirectReferences_MissingIndirectDependency_OK()
    {
        // Arrange
        var ckModelIdA = new CkModelId("A-1.0.0");
        var ckModelIdB = new CkModelId("B-1.0.0");
        var ckModelIdC = new CkModelId("C-1.0.0");
        var dependenciesA = new List<CkModelId> { ckModelIdB };
        var dependenciesB = new List<CkModelId> { ckModelIdC };
        var expectedModelA = CreateTestCkModel(ckModelIdA, dependenciesA.ToArray());
        var expectedModelB = CreateTestCkModel(ckModelIdB, dependenciesB.ToArray());

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdA.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdA });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdB.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdB });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdC.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = false, ModelId = null });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdA, _operationResult, null, null))
            .Returns(expectedModelA);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdB, _operationResult, null, null))
            .Returns(expectedModelB);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdC, _operationResult, null, null))
            .Returns((CkCompiledModelRoot?)null);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.SoftResolveDependenciesAsync(
            new List<CkModelId> { ckModelIdA }, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.RootDependencyModelIds);
        Assert.Single(result.UnresolvedDependencyModelIds);
        Assert.Contains(ckModelIdC.ToVersionRange(), result.UnresolvedDependencyModelIds);
        Assert.Equal(2, result.SkippedModelIds.Count);
        Assert.Contains(ckModelIdA, result.SkippedModelIds);
        Assert.Contains(ckModelIdB, result.SkippedModelIds);
    }

    [Fact]
    public async Task SoftResolveDependenciesAsync_IndirectReferences_MissingMultipleIndirectDependency_OK()
    {
        // Arrange
        var ckModelIdA = new CkModelId("A-1.0.0");
        var ckModelIdB = new CkModelId("B-1.0.0");
        var ckModelIdC = new CkModelId("C-1.0.0");
        var ckModelIdD = new CkModelId("D-1.0.0");
        var dependenciesB = new List<CkModelId> { ckModelIdD };
        var dependenciesC = new List<CkModelId> { ckModelIdD };
        var expectedModelA = CreateTestCkModel(ckModelIdA);
        var expectedModelB = CreateTestCkModel(ckModelIdB, dependenciesB.ToArray());
        var expectedModelC = CreateTestCkModel(ckModelIdC, dependenciesC.ToArray());

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdA.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdA });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdB.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdB });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdC.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdC });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdD.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = false, ModelId = null });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdA, _operationResult, null, null))
            .Returns(expectedModelA);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdB, _operationResult, null, null))
            .Returns(expectedModelB);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdC, _operationResult, null, null))
            .Returns(expectedModelC);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdD, _operationResult, null, null))
            .Returns((CkCompiledModelRoot?)null);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.SoftResolveDependenciesAsync(
            new List<CkModelId> { ckModelIdA, ckModelIdB }, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.RootDependencyModelIds);
        Assert.Contains(ckModelIdA, result.RootDependencyModelIds);
        Assert.Single(result.UnresolvedDependencyModelIds);
        Assert.Contains(ckModelIdD.ToVersionRange(), result.UnresolvedDependencyModelIds);
        Assert.Single(result.SkippedModelIds);
        Assert.Contains(ckModelIdB, result.SkippedModelIds);
    }

    [Fact]
    public async Task SoftResolveDependenciesAsync_IndirectReferences_MissingMultipleIndirectDependencyOnRoot_OK()
    {
        // Arrange
        var ckModelIdA = new CkModelId("A-1.0.0");
        var ckModelIdB = new CkModelId("B-1.0.0");
        var ckModelIdC = new CkModelId("C-1.0.0");
        var ckModelIdD = new CkModelId("D-1.0.0");
        var dependenciesB = new List<CkModelId> { ckModelIdD };
        var dependenciesC = new List<CkModelId> { ckModelIdD };
        var expectedModelA = CreateTestCkModel(ckModelIdA);
        var expectedModelB = CreateTestCkModel(ckModelIdB, dependenciesB.ToArray());
        var expectedModelC = CreateTestCkModel(ckModelIdC, dependenciesC.ToArray());

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdA.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdA });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdB.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdB });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdC.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdC });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdD.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = false, ModelId = null });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdA, _operationResult, null, null))
            .Returns(expectedModelA);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdB, _operationResult, null, null))
            .Returns(expectedModelB);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdC, _operationResult, null, null))
            .Returns(expectedModelC);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdD, _operationResult, null, null))
            .Returns((CkCompiledModelRoot?)null);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.SoftResolveDependenciesAsync(
            new List<CkModelId> { ckModelIdA, ckModelIdB, ckModelIdC }, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.RootDependencyModelIds);
        Assert.Contains(ckModelIdA, result.RootDependencyModelIds);
        Assert.Single(result.UnresolvedDependencyModelIds);
        Assert.Contains(ckModelIdD.ToVersionRange(), result.UnresolvedDependencyModelIds);
        Assert.Equal(2, result.SkippedModelIds.Count);
        Assert.Contains(ckModelIdB, result.SkippedModelIds);
        Assert.Contains(ckModelIdC, result.SkippedModelIds);
    }

    [Fact]
    public async Task
        SoftResolveDependenciesAsync_IndirectReferences_MissingMultipleIndirectDependencyOnRoot_SkipCascading_OK()
    {
        // Arrange
        var ckModelIdA = new CkModelId("A-1.0.0");
        var ckModelIdB = new CkModelId("B-1.0.0");
        var ckModelIdC = new CkModelId("C-1.0.0");
        var ckModelIdD = new CkModelId("D-1.0.0");
        var dependenciesA = new List<CkModelId> { ckModelIdD };
        var dependenciesB = new List<CkModelId> { ckModelIdA };
        var dependenciesC = new List<CkModelId> { ckModelIdA };
        var expectedModelA = CreateTestCkModel(ckModelIdA, dependenciesA.ToArray());
        var expectedModelB = CreateTestCkModel(ckModelIdB, dependenciesB.ToArray());
        var expectedModelC = CreateTestCkModel(ckModelIdC, dependenciesC.ToArray());

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdA.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdA });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdB.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdB });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdC.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdC });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdD.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = false, ModelId = null });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdA, _operationResult, null, null))
            .Returns(expectedModelA);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdB, _operationResult, null, null))
            .Returns(expectedModelB);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdC, _operationResult, null, null))
            .Returns(expectedModelC);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdD, _operationResult, null, null))
            .Returns((CkCompiledModelRoot?)null);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.SoftResolveDependenciesAsync(
            new List<CkModelId> { ckModelIdA, ckModelIdB, ckModelIdC }, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.RootDependencyModelIds);
        Assert.Equal(3, result.ResolvedDependentModelIds.Count);
        Assert.Single(result.UnresolvedDependencyModelIds);
        Assert.Contains(ckModelIdD.ToVersionRange(), result.UnresolvedDependencyModelIds);
        Assert.Equal(3, result.SkippedModelIds.Count);
        Assert.Contains(ckModelIdA, result.SkippedModelIds);
        Assert.Contains(ckModelIdB, result.SkippedModelIds);
        Assert.Contains(ckModelIdC, result.SkippedModelIds);
    }

        [Fact]
    public async Task
        SoftResolveDependenciesAsync_IndirectReferences_UpdateBasicConstructionKitModel_OK()
    {
        // Arrange
        var ckModelIdA1 = new CkModelId("A-1.0.0");
        var ckModelIdA2 = new CkModelId("A-1.0.1");
        var ckModelIdB = new CkModelId("B-1.0.0");
        var ckModelIdC = new CkModelId("C-1.0.0");
        var ckModelIdD = new CkModelId("D-1.0.0");
        var dependenciesB = new List<CkModelId> { ckModelIdA1 };
        var dependenciesC = new List<CkModelId> { ckModelIdA1 };
        var dependenciesD = new List<CkModelId> { ckModelIdC };
        var expectedModelA2 = CreateTestCkModel(ckModelIdA2);
        var expectedModelB = CreateTestCkModel(ckModelIdB, dependenciesB.ToArray());
        var expectedModelC = CreateTestCkModel(ckModelIdC, dependenciesC.ToArray());
        var expectedModelD = CreateTestCkModel(ckModelIdD, dependenciesD.ToArray());

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdA1.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = false, ModelId = null });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdA2.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdA2 });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdB.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdB });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdC.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdC });
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(ckModelIdD.ToVersionRange(), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = ckModelIdD });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdA1, _operationResult, null, null))
            .Returns((CkCompiledModelRoot?)null);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdA2, _operationResult, null, null))
            .Returns(expectedModelA2);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdB, _operationResult, null, null))
            .Returns(expectedModelB);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdC, _operationResult, null, null))
            .Returns(expectedModelC);
        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(ckModelIdD, _operationResult, null, null))
            .Returns(expectedModelD);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.SoftResolveDependenciesAsync(
            new List<CkModelId> { ckModelIdA2, ckModelIdC, ckModelIdD }, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.RootDependencyModelIds);
        Assert.Contains(ckModelIdA2, result.RootDependencyModelIds);
        Assert.Equal(3, result.ResolvedDependentModelIds.Count);
        Assert.Single(result.UnresolvedDependencyModelIds);
        Assert.Contains(ckModelIdA1.ToVersionRange(), result.UnresolvedDependencyModelIds);
        Assert.Equal(2, result.SkippedModelIds.Count);
        Assert.Contains(ckModelIdC, result.SkippedModelIds);
        Assert.Contains(ckModelIdD, result.SkippedModelIds);
    }

    [Fact]
    public async Task SoftResolveDependenciesAsync_WithUnresolvableDependency_ReturnsUnresolvedInResult()
    {
        // Arrange
        var dependencyModelId = new CkModelId("NonExistentModel-1.0.0");
        var dependencies = new List<CkModelId> { dependencyModelId };

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(A<CkModelIdVersionRange>._, null))
            .Returns(new ModelExistingResult { Exists = false, ModelId = null });

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.SoftResolveDependenciesAsync(
            dependencies, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.RootDependencyModelIds);
        Assert.Single(result.UnresolvedDependencyModelIds);
        Assert.Empty(result.SkippedModelIds);
    }

    [Fact]
    public async Task HardResolveDependenciesAsync_WithTransitiveDependencies_ResolvesAllDependencies()
    {
        // Arrange
        var rootDependency = new CkModelId("RootModel-1.0.0");
        var childDependency = new CkModelId("ChildModel-1.0.0");
        var dependencies = new List<CkModelId> { rootDependency };

        var rootModel = CreateTestCkModel(rootDependency, [childDependency]);
        var childModel = CreateTestCkModel(childDependency);

        A.CallTo(() =>
                _repositoryManagementService.IsExistingAsync(
                    A<CkModelIdVersionRange>.That.Matches(r => r.Name.Equals(rootDependency.Name)), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = rootDependency });

        A.CallTo(() =>
                _repositoryManagementService.IsExistingAsync(
                    A<CkModelIdVersionRange>.That.Matches(r => r.Name.Equals(childDependency.Name)), null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = childDependency });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(rootDependency, _operationResult, null, null))
            .Returns(rootModel);

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(childDependency, _operationResult, null, null))
            .Returns(childModel);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.HardResolveDependenciesAsync(
            dependencies, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult);

        // Assert
        Assert.Single(result);
        Assert.Contains(rootDependency, result);
        Assert.Empty(_operationResult.Messages);

        // Verify both models were added to the graph
        Assert.Contains(rootDependency, _modelGraph.Dependencies.Keys);
        Assert.Contains(childDependency, _modelGraph.Dependencies.Keys);
    }

    [Fact]
    public async Task HardResolveDependenciesAsync_WithNullModelFromRepository_ThrowException()
    {
        // Arrange
        var dependencyVersionRange = new CkModelIdVersionRange("TestModel-1.0.0");
        var dependencyVersionRanges = new List<CkModelIdVersionRange> { dependencyVersionRange };
        var expectedModelId = new CkModelId("TestModel-1.0.0");

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(dependencyVersionRange, null))
            .Returns(new ModelExistingResult { Exists = true, ModelId = expectedModelId });

        A.CallTo(() => _repositoryManagementService.TryLookupCkModelAsync(expectedModelId, _operationResult, null, null))
            .Returns((CkCompiledModelRoot?)null);

        A.CallTo(() => _originFileResolver.Resolve(dependencyVersionRange))
            .Returns("test-file.yaml");

        // Act
        await Assert.ThrowsAsync<ModelValidationException>(async ()=> await _resolver.HardResolveDependenciesAsync(
            dependencyVersionRanges, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult));

        // Assert
        Assert.Single(_operationResult.Messages);
        Assert.Equal(1, _operationResult.Messages.First().MessageNumber);
    }

    [Fact]
    public async Task HardResolveDependenciesAsync_WithSourceIdentifier_PassesSourceIdentifierToRepository()
    {
        // Arrange
        var dependencyVersionRange = new CkModelIdVersionRange("TestModel-1.0.0");
        var dependencyVersionRanges = new List<CkModelIdVersionRange> { dependencyVersionRange };
        var expectedModelId = new CkModelId("TestModel-1.0.0");
        var expectedModel = CreateTestCkModel(expectedModelId);
        var sourceIdentifier = new object();

        A.CallTo(() => _repositoryManagementService.IsExistingAsync(dependencyVersionRange, sourceIdentifier))
            .Returns(new ModelExistingResult { Exists = true, ModelId = expectedModelId });

        A.CallTo(() =>
                _repositoryManagementService.TryLookupCkModelAsync(expectedModelId, _operationResult, sourceIdentifier, null))
            .Returns(expectedModel);

        A.CallTo(() => _originFileResolver.Resolve(A<object>._))
            .Returns("test-file.yaml");

        // Act
        var result = await _resolver.HardResolveDependenciesAsync(
            dependencyVersionRanges, _modelGraph, _variableResolver,
            _originFileResolver, _operationResult, sourceIdentifier);

        // Assert
        Assert.Single(result);
        A.CallTo(() => _repositoryManagementService.IsExistingAsync(dependencyVersionRange, sourceIdentifier))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() =>
                _repositoryManagementService.TryLookupCkModelAsync(expectedModelId, _operationResult, sourceIdentifier, null))
            .MustHaveHappenedOnceExactly();
    }

    private static CkCompiledModelRoot CreateTestCkModel(CkModelId modelId, CkModelId[]? dependencies = null)
    {
        return new CkCompiledModelRoot
        {
            ModelId = modelId,
            Dependencies = dependencies?.ToList()
        };
    }
}