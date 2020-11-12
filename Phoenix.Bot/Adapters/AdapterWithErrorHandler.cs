using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Phoenix.Bot.Adapters
{
    public class AdapterWithErrorHandler : BotFrameworkHttpAdapter
    {
        public AdapterWithErrorHandler(
            IConfiguration configuration,
            ILogger<BotFrameworkHttpAdapter> logger,
            EntityFrameworkTranscriptStore transcriptStore)
            : base(configuration, logger)
        {
            OnTurnError = async (turnContext, exception) =>
            {
                if (turnContext.Activity.ChannelId != "emulator")
                {
                    var act = turnContext.Activity;
                    act.Value = exception.Message;
                    await transcriptStore.LogActivityAsync(act);
                }

                // Log any leaked exception from the application.
                logger.LogError(exception, $"[OnTurnError] unhandled error : {exception.Message}");

                // Send a message to the user
                await turnContext.SendActivityAsync("Λυπάμαι, υπήρξε ένα πρόβλημα :(");

                // Send a trace activity, which will be displayed in the Bot Framework Emulator
                await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message, "https://www.botframework.com/schemas/error", "TurnError");
            };
        }
    }
}
