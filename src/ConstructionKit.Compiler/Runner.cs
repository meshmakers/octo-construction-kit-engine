using System.Reflection;
using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler;

internal class Runner
{
    private readonly ILogger<Runner> _logger;
    private readonly ICommandParser _parser;

    public Runner(ILogger<Runner> logger, ICommandParser parser)
    {
        _logger = logger;
        _parser = parser;
    }

    public async Task<int> DoActionAsync()
    {
        try
        {
            _logger.LogInformation("Octo Mesh Construction Kit Compiler, Version {ProductVersion}",
                GetProductVersion());
            _logger.LogInformation("{Copyright}", GetCopyright());

            await _parser.ParseAndValidateAsync();

            return 0;
        }
        catch (CompilerException ex)
        {
            _logger.LogError("{Message}", ex.Message);

            ex.OperationResult.WriteMessagesToLogger(_logger);

            return -5;
        }
        catch (ModelParseException ex)
        {
            _logger.LogError("{Message}", ex.Message);

            ex.OperationResult.WriteMessagesToLogger(_logger);

            return -4;
        }
        catch (CkModelException ex)
        {
            _logger.LogError("{Message}", ex.Message);

            return -3;
        }
        catch (ArgumentValueMissingException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            _parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (MandatoryArgumentsMissingException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            _parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (InvalidProgramException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            _parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (Exception ex)
        {
            var tmp = ex;
            while (tmp != null)
            {
                _logger.LogCritical(tmp, "{Message}", tmp.Message);
                tmp = tmp.InnerException;
            }

            return -99;
        }
    }

    private static string GetProductVersion()
    {
        var attribute = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyFileVersionAttribute>()
            .Single();
        return attribute.Version;
    }

    private static string GetCopyright()
    {
        var attribute = Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyCopyrightAttribute>()
            .Single();

        return attribute.Copyright;
    }
}