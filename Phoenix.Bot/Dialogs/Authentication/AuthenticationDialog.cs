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
using Phoenix.DataHandle.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Bot.Builder;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options.Authentification;
using Microsoft.EntityFrameworkCore;

namespace Phoenix.Bot.Dialogs.Authentication
{
    public class AuthenticationDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly AspNetUserRepository userRepository;
        private readonly UserManager<ApplicationUser> userManager;
        private readonly IStatePropertyAccessor<UserData> userDataAccesor;
        private readonly ApplicationStore appStore;

        public AuthenticationDialog(IConfiguration configuration, PhoenixContext phoenixContext, ApplicationDbContext appContext,
            UserManager<ApplicationUser> userManager, UserState userState,
            CredentialsDialog credentialsDialog)
            : base(nameof(AuthenticationDialog))
        {
            this.configuration = configuration;
            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.userRepository.Include(uq => uq.Include(u => u.User));
            this.userManager = userManager;
            this.userDataAccesor = userState.CreateProperty<UserData>(nameof(UserData));
            this.appStore = new ApplicationStore(appContext);

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
            return await stepContext.BeginDialogAsync(nameof(CredentialsDialog), new CredentialsOptions(), cancellationToken);
        }

        private async Task<DialogTurnResult> LoginStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is null)
                return await stepContext.EndDialogAsync(false, cancellationToken);

            var credentialsOptions = stepContext.Result as CredentialsOptions;

            if (credentialsOptions.Verified)
            {
                var verifiedUser = await userRepository.Find(credentialsOptions.VerifiedUserId.Value);

                verifiedUser.User.TermsAccepted = true;
                verifiedUser.PhoneNumberConfirmed = credentialsOptions.IsOwnerAuthentication;
                userRepository.Update(verifiedUser);

                var provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
                var providerKey = stepContext.Context.Activity.From.Id;
                userRepository.LinkLogin(provider, providerKey, verifiedUser.Id, activate: true);

                // Create password for Teachers to log into the extension
                if (userRepository.FindRoles(verifiedUser).Any(r => r.Type.IsStaff() || r.Type.IsBackend()))
                {
                    var appUser = await appStore.FindByIdAsync(verifiedUser.Id.ToString());
                    if (!await userManager.HasPasswordAsync(appUser))
                    {
                        string password = DialogsHelper.GeneratePasscode(8);
                        await userManager.AddPasswordAsync(appUser, password);

                        var userData = await userDataAccesor.GetAsync(stepContext.Context, null, cancellationToken);
                        userData.RevealExtensionPassword = true;
                        userData.TempExtensionPassword = password;
                        await userDataAccesor.SetAsync(stepContext.Context, userData, cancellationToken);
                    }
                }
            }
            
            return await stepContext.EndDialogAsync(credentialsOptions.Verified, cancellationToken);
        }
    }
}
