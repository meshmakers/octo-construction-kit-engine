using Meshmakers.Octo.ConstructionKit.Compiler.SystemTests.Fixtures;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Meshmakers.Octo.ConstructionKit.Compiler.SystemTests;

public class CompilerServiceTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public CompilerServiceTests(TemporaryDirectoryFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task CreateNew_ok()
    {
        await using (var serviceProvider = _fixture.Services.BuildServiceProvider())
        {
            var rootPath = _fixture.CreateTempDirectory();
            var compilerService = serviceProvider.GetRequiredService<ICompilerService>();
            await compilerService.CreateNewAsync(rootPath);

            Assert.True(Directory.Exists(rootPath));
            Assert.True(File.Exists(Path.Combine(rootPath, CompilerStatics.MetadataFile)));
            Assert.True(Directory.Exists(Path.Combine(rootPath, CompilerStatics.TypesDirectoryName)));
            Assert.True(Directory.Exists(Path.Combine(rootPath, CompilerStatics.AssociationsDirectoryName)));
            Assert.True(Directory.Exists(Path.Combine(rootPath, CompilerStatics.AttributesDirectoryName)));
        }
    }

    [Fact]
    public async Task CreateNew_NonEmptyDirectory_fail()
    {
        await using (var serviceProvider = _fixture.Services.BuildServiceProvider())
        {
            var rootPath = _fixture.CreateTempDirectory();
            Directory.CreateDirectory(rootPath);
            File.Create(Path.Combine(rootPath, "test.txt")).Close();
            var compilerService = serviceProvider.GetRequiredService<ICompilerService>();
            await Assert.ThrowsAsync<CompilerException>(async () => await compilerService.CreateNewAsync(rootPath));
        }
    }


    [Fact]
    public async Task Compile_ok()
    {
        await using (var serviceProvider = _fixture.Services.BuildServiceProvider())
        {
            var rootPath = _fixture.CreateTempDirectory();
            _testOutputHelper.WriteLine($"Directory: {rootPath}");

            var compilerService = serviceProvider.GetRequiredService<ICompilerService>();
            await compilerService.CreateNewAsync(rootPath);
            await compilerService.CompileAsync(rootPath, rootPath, null);

            Assert.True(Directory.Exists(rootPath));
            Assert.True(File.Exists(Path.Combine(rootPath, "ck-sample1.yaml")));
            Assert.False(File.Exists(Path.Combine(rootPath, "ck-sample1.cache.json")));
        }
    }

    [Fact]
    public async Task Compile_WithCache_ok()
    {
        await using (var serviceProvider = _fixture.Services.BuildServiceProvider())
        {
            var rootPath = _fixture.CreateTempDirectory();
            _testOutputHelper.WriteLine($"Directory: {rootPath}");

            var compilerService = serviceProvider.GetRequiredService<ICompilerService>();
            await compilerService.CreateNewAsync(rootPath);
            await compilerService.CompileAsync(rootPath, rootPath, rootPath);

            Assert.True(Directory.Exists(rootPath));
            Assert.True(File.Exists(Path.Combine(rootPath, "ck-sample1.yaml")));
            Assert.True(File.Exists(Path.Combine(rootPath, "ck-sample1.cache.json")));
        }
    }
}