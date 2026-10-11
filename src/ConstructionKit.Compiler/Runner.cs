using Meshmakers.Common.CommandLineParser;
using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler;

internal class Runner(ILogger<Runner> logger, ICommandParser parser)
{
    public async Task<int> DoActionAsync()
    {
        try
        {
            await parser.ParseAndValidateAsync();

            return 0;
        }
        catch (CkPublishRefusedException ex)
        {
            // AB#6389: the publish gate refused the model. Summary first, then one line per reason (ordered,
            // deduplicated), then the process ends with the code of every other verdict (-6).
            logger.LogError("{Message}", ex.Message);

            ex.OperationResult.WriteMessagesToLogger(logger);

            return -6;
        }
        catch (ModelValidationException ex) 
        {
            logger.LogError("{Message}", ex.Message);
            
            return -6;
        }
        catch (CompilerException ex)
        {
            logger.LogError("{Message}", ex.Message);

            ex.OperationResult.WriteMessagesToLogger(logger);

            return -5;
        }
        catch (ModelParseException ex)
        {
            logger.LogError("{Message}", ex.Message);

            ex.OperationResult.WriteMessagesToLogger(logger);

            return -4;
        }
        catch (CkModelException ex)
        {
            logger.LogError("{Message}", ex.Message);

            return -3;
        }
        catch (ArgumentValueMissingException ex)
        {
            logger.LogError("{Message}", ex.Message);
            parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (MandatoryArgumentsMissingException ex)
        {
            logger.LogError("{Message}", ex.Message);
            parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (InvalidParameterException ex)
        {
            logger.LogError("{Message}", ex.Message);
            parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (InvalidProgramException ex)
        {
            logger.LogError("{Message}", ex.Message);
            parser.ShowUsageInformation(Constants.OctoExeName);
            return -1;
        }
        catch (Exception ex)
        {
            var tmp = ex;
            while (tmp != null)
            {
                logger.LogCritical(tmp, "{Message}", tmp.Message);
                tmp = tmp.InnerException;
            }

            return -99;
        }
    }
}