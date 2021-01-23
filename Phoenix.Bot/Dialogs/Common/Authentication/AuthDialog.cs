using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Extensions.Configuration;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs;
using System.Threading.Tasks;
using System.Threading;
using Phoenix.DataHandle.Repositories;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Phoenix.DataHandle.Main;
using Phoenix.Bot.Utilities.State.Options;

namespace Phoenix.Bot.Dialogs.Common.Authentication
{
    public class AuthDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly AspNetUserRepository userRepository;

        public AuthDialog(IConfiguration configuration, PhoenixContext phoenixContext,
            CredentialsDialog credentialsDialog)
            : base(nameof(AuthDialog))
        {
            this.configuration = configuration;
            this.userRepository = new AspNetUserRepository(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new NumberPrompt<long>(PromptNames.Phone, PromptValidators.PhoneNumberPromptValidator));

            AddDialog(credentialsDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.Top,
                new WaterfallStep[]
                {
                    AskForCredentialsStepAsync,
                    LoginStepAsync
                }));
            
            InitialDialogId = WaterfallNames.Auth.Top;
        }

        private async Task<DialogTurnResult> AskForCredentialsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Αρχικά, θα χρειαστώ το κινητό τηλέφωνο που δώθηκε κατά την εγγραφή.");
            return await stepContext.BeginDialogAsync(nameof(CredentialsDialog), new AuthenticationOptions(), cancellationToken);
        }

        private async Task<DialogTurnResult> LoginStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var authenticationOptions = stepContext.Result as AuthenticationOptions;

            if (authenticationOptions.Verified)
            {
                var verifiedUser = userRepository.Find().
                    Where(u => u.PhoneNumber == authenticationOptions.Phone).
                    Where(u => u.UserSchool.Any(us => us.School.FacebookPageId == stepContext.Context.Activity.Recipient.Id)).
                    Single(u => u.User.IdentifierCode == authenticationOptions.VerifiedCode);

                var providerName = stepContext.Context.Activity.ChannelId.ToLoginProvider().GetProviderName();
                var login = new AspNetUserLogins()
                {
                    LoginProvider = providerName,
                    ProviderDisplayName = providerName.ToLower(),
                    ProviderKey = stepContext.Context.Activity.From.Id,
                    IsActive = true,
                    UserId = verifiedUser.Id
                };

                userRepository.LinkLogin(login);
            }
            
            return await stepContext.EndDialogAsync(authenticationOptions.Verified, cancellationToken);
        }
    }
}
