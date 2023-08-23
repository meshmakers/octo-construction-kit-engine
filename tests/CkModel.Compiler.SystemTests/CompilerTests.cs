using CkModel.Compiler.SystemTests.Fixtures;
using Meshmakers.Octo.SystematizedData.CkModel.Compiler;
using Meshmakers.Octo.SystematizedData.CkModel.Contracts;
using Meshmakers.Octo.SystematizedData.CkModel.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CkModel.Compiler.SystemTests;

public class CompilerTests : IClassFixture<TemporaryDirectoryFixture>
{
    private readonly TemporaryDirectoryFixture _fixture;
    private readonly ITestOutputHelper _testOutputHelper;

    public CompilerTests(TemporaryDirectoryFixture fixture, ITestOutputHelper testOutputHelper)
    {
        _fixture = fixture;
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task CreateNew_ok()
    {
        await using (var serviceProvider = _fixture.Services.BuildServiceProvider())
        {
            string rootPath = _fixture.CreateTempDirectory();
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
            string rootPath = _fixture.CreateTempDirectory();
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
            string rootPath = _fixture.CreateTempDirectory();
            _testOutputHelper.WriteLine($"Directory: {rootPath}");

            var compilerService = serviceProvider.GetRequiredService<ICompilerService>();
            await compilerService.CreateNewAsync(rootPath);
            await compilerService.CompileAsync(rootPath);

            Assert.True(Directory.Exists(rootPath));
            Assert.True(File.Exists(Path.Combine(rootPath, CompilerStatics.MetadataFile)));
            Assert.True(Directory.Exists(Path.Combine(rootPath, CompilerStatics.TypesDirectoryName)));
            Assert.True(Directory.Exists(Path.Combine(rootPath, CompilerStatics.AssociationsDirectoryName)));
            Assert.True(Directory.Exists(Path.Combine(rootPath, CompilerStatics.AttributesDirectoryName)));
        }
    }
}