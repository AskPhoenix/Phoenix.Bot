using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Bot.Connector.Authentication;
using Phoenix.Bot.Utilities.Errors;
using Phoenix.DataHandle.Main.Types;
using Phoenix.Language.Bot.Types.BotError;

namespace Phoenix.Bot.Adapters
{
    public class AdapterWithErrorHandler : CloudAdapter
    {
        public AdapterWithErrorHandler(
            BotFrameworkAuthentication auth,
            ConversationState conversationState,
            EntityFrameworkTranscriptStore transcriptStore,
            ILogger<IBotFrameworkHttpAdapter> logger)
            : base(auth, logger)
        {
            OnTurnError = async (turnCtx, exception) =>
            {
                // Store the Activity and the Exception Message in BotTranscript Table
                if (turnCtx.Activity.ChannelId.ToChannelProvider() != ChannelProvider.Emulator)
                {
                    var activity = turnCtx.Activity;
                    activity.Value = exception.Message;
                    await transcriptStore.LogActivityAsync(activity);
                }

                // Delete the Conversation State
                await conversationState.DeleteAsync(turnCtx);
                await conversationState.SaveChangesAsync(turnCtx);

                if (exception is BotException botException)
                {
                    await turnCtx.SendActivityAsync(botException.Message);
                    await turnCtx.SendActivityAsync(botException.Solution);
                    await turnCtx.SendActivityAsync(MessageFactory.SuggestedActions(
                        new string[1] { "🏠 Αρχική" }, $"({ErrorResources.Code}: {botException.Code})"));

                    logger.LogError(exception, "Bot Error {Name}: {Code}",
                        botException.Error.ToString(), botException.Code);

                    return;
                }

                // Send a message to the user
                await turnCtx.SendActivityAsync(MessageFactory.SuggestedActions(
                    new string[1] { "🏠 Αρχική" }, BotError.Unknown.GetMessage() + " 😓"));

                // Log any leaked exception from the application.
                logger.LogError(exception, "[OnTurnError] unhandled error : {Msg}", exception.Message);

                // Send a trace activity, which will be displayed in the Bot Framework Emulator
                await turnCtx.TraceActivityAsync("OnTurnError Trace", exception.Message,
                    "https://www.botframework.com/schemas/error", "TurnError");

                // TODO: Restart Main Dialog
            };
        }
    }
}
