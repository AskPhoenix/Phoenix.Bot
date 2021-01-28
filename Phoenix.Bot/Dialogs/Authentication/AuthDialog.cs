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
using System;
using Phoenix.DataHandle.Identity;
using Microsoft.AspNetCore.Identity;

namespace Phoenix.Bot.Dialogs.Authentication
{
    public class AuthDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly AspNetUserRepository userRepository;
        private readonly UserManager<ApplicationUser> userManager;
        private readonly ApplicationStore appStore;

        public AuthDialog(IConfiguration configuration, PhoenixContext phoenixContext, UserManager<ApplicationUser> userManager, ApplicationDbContext appContext,
            CredentialsDialog credentialsDialog)
            : base(nameof(AuthDialog))
        {
            this.configuration = configuration;
            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.userManager = userManager;
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

                verifiedUser.PhoneNumberConfirmed = true;
                userRepository.Update(verifiedUser);

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

                // Create password for Teachers to log into the extension
                if (userRepository.HasRole(verifiedUser, Role.Teacher))
                {
                    var appUser = await appStore.FindByIdAsync(verifiedUser.Id.ToString());
                    if (!await userManager.HasPasswordAsync(appUser))
                    {
                        string password = Convert.ToBase64String(Guid.NewGuid().ToByteArray()).Substring(0, 8);
                        await userManager.AddPasswordAsync(appUser, password);
                    }
                }
            }
            
            return await stepContext.EndDialogAsync(authenticationOptions.Verified, cancellationToken);
        }
    }
}
