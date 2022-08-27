using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Bot.Connector.Authentication;
using Phoenix.Language.Bot.Types.BotError;

namespace Phoenix.Bot.Adapters
{
    public class AdapterWithErrorHandler : CloudAdapter
    {
        public AdapterWithErrorHandler(
            BotFrameworkAuthentication auth,
            ILogger<AdapterWithErrorHandler> logger,
            ConversationState convState,
            EntityFrameworkTranscriptStore transcriptStore)
            : base(auth, logger)
        {
            OnTurnError = async (turnCtx, exception) =>
            {
                // Store the Activity and the Exception Message in BotTranscript Table
                if (turnCtx.Activity.ChannelId.ToChannelProvider() != ChannelProvider.Emulator)
                {
                    turnCtx.Activity.Value = exception.Message;
                    await transcriptStore.LogActivityAsync(turnCtx.Activity);
                }

                // Delete the Conversation State
                await convState.DeleteAsync(turnCtx);
                await convState.SaveChangesAsync(turnCtx);

                // Show Error code
                if (exception is BotException botException)
                {
                    await turnCtx.SendActivityAsync(botException.Message);

                    if (!botException.ShowMessageOnly)
                    {
                        await turnCtx.SendActivityAsync(botException.Solution);
                        await turnCtx.SendActivityAsync(MessageFactory.SuggestedActions(
                            new string[1] { "🏠 Αρχική" }, $"({ErrorResources.Code}: {botException.Code})"));
                    }

                    logger.LogError(exception, "Bot Error {Name}: {Code}",
                        botException.Error.ToString(), botException.Code);
                }
                else
                {
                    // Send a message to the user
                    await turnCtx.SendActivityAsync(MessageFactory.SuggestedActions(
                        new string[1] { "🏠 Αρχική" }, BotError.Unknown.GetMessage() + " 😓"));

                    // Log any leaked exception from the application
                    logger.LogError(exception, "[OnTurnError] unhandled error : {Msg}", exception.Message);
                }

                // Send a trace activity, which will be displayed in the Bot Framework Emulator
                await turnCtx.TraceActivityAsync("OnTurnError Trace", exception.Message,
                    "https://www.botframework.com/schemas/error", "TurnError");
            };
        }
    }
}
