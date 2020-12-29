using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Extensions.Configuration;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.Dialogs;
using System.Threading.Tasks;
using System.Threading;
using System;
using Phoenix.Bot.Utilities.State.DialogOptions;

namespace Phoenix.Bot.Dialogs.Common.Authentication
{
    public class AuthDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly IStatePropertyAccessor<UserOptions> userOptionsAccesor;
        private readonly IStatePropertyAccessor<ConversationsOptions> convOptionsAccesor;

        public AuthDialog(IConfiguration configuration, ConversationState conversationState, UserState userState,
            CredentialsDialog credentialsDialog)
            : base(nameof(CredentialsDialog))
        {
            this.configuration = configuration;
            this.userOptionsAccesor = userState.CreateProperty<UserOptions>(UserDefaults.PropertyName);
            this.convOptionsAccesor = conversationState.CreateProperty<ConversationsOptions>(ConversationDefaults.PropertyName);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new NumberPrompt<long>(PromptNames.Phone, PromptValidators.PhoneNumberPromptValidator));

            AddDialog(credentialsDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.Top,
                new WaterfallStep[]
                {
                    AskForCredentialsStepAsync,
                    RegisterStepAsync
                }));
            
            InitialDialogId = WaterfallNames.Auth.Top;
        }

        private async Task<DialogTurnResult> AskForCredentialsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Αρχικά, θα χρειαστώ το κινητό τηλέφωνο του γονέα που δώθηκε κατά την εγγραφή.");

            return await stepContext.BeginDialogAsync(nameof(CredentialsDialog), new AuthenticationOptions(), cancellationToken);
        }

        private Task<DialogTurnResult> RegisterStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
