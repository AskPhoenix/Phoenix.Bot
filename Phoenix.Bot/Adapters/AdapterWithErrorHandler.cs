using Bot.Builder.Community.Storage.EntityFramework;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Logging;
using Phoenix.DataHandle.Main.Types;

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
            OnTurnError = async (turnContext, exception) =>
            {
                // Store the Activity and the Exception Message in BotTranscript Table
                if (turnContext.Activity.ChannelId.ToChannelProvider() != ChannelProvider.Emulator)
                {
                    var activity = turnContext.Activity;
                    activity.Value = exception.Message;
                    await transcriptStore.LogActivityAsync(activity);
                }

                // Delete the Conversation State
                await conversationState.DeleteAsync(turnContext);
                await conversationState.SaveChangesAsync(turnContext);

                // Log any leaked exception from the application.
                logger.LogError(exception, "[OnTurnError] unhandled error : {Msg}", exception.Message);

                // Send a message to the user
                await turnContext.SendActivityAsync(MessageFactory.SuggestedActions(
                    new string[1] { "🏠 Αρχική" }, "Λυπάμαι, υπήρξε ένα πρόβλημα :("));
                
                // Send a trace activity, which will be displayed in the Bot Framework Emulator
                await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message,
                    "https://www.botframework.com/schemas/error", "TurnError");
            };
        }
    }
}
