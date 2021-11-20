using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.DataHandle.Repositories;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.DataHandle.Main;
using Phoenix.Bot.Utilities.State.Options.Authentification;

namespace Phoenix.Bot.Dialogs.Authentication
{
    public class CredentialsDialog : ComponentDialog
    {
        private readonly AspNetUserRepository userRepository;

        public CredentialsDialog(PhoenixContext phoenixContext,
            VerificationDialog verificationDialog)
            : base(nameof(CredentialsDialog))
        {
            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.userRepository.Include(uq => uq.
                Include(u => u.User).
                Include(u => u.AspNetUserLogins).
                Include(u => u.UserSchool));

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));
            AddDialog(new NumberPrompt<long>(PromptNames.Phone, PromptValidators.PhoneNumberPromptValidator));

            AddDialog(verificationDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Auth.Credentials.Phone,
                new WaterfallStep[]
                {
                    AskPhoneStepAsync,
                    EligibilityStepAsync,
                    VerificationStepAsync,
                    PostVerificationStepAsync
                }));

            InitialDialogId = WaterfallNames.Auth.Credentials.Phone;
        }

        #region Phone Waterfall Dialog

        private async Task<DialogTurnResult> AskPhoneStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                PromptNames.Phone,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Παρακαλώ πληκτρολόγησε τον αριθμό παρακάτω:"),
                    RetryPrompt = MessageFactory.Text("Ο αριθμός τηλεφώνου πρέπει να είναι στη μορφή 69xxxxxxxx. Παρακαλώ πληκτρολόγησέ τον ξανά:")
                });
        }

        private async Task<DialogTurnResult> EligibilityStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            Activity activity = stepContext.Context.Activity;
            string phone = stepContext.Result.ToString();

            var credentialsOptions = stepContext.Options as CredentialsOptions;
            credentialsOptions.Phone = phone;

            var phoneOwner = await userRepository.Find(checkUnique: u => u.PhoneNumber == phone && u.User.IsSelfDetermined);

            //TODO: Move to Repository
            bool isAssignedToCurrentSchool = phoneOwner is not null && 
                (phoneOwner.UserSchool?.Any(us => us.School.FacebookPageId == activity.Recipient.Id) ?? false);

            if (!isAssignedToCurrentSchool)
            {
                bool isSuper = userRepository.FindRoles(phoneOwner).Any(r => r.Type.IsSuper());
                if (!isSuper)
                {
                    await stepContext.Context.SendActivityAsync("Το κινητό τηλέφωνο δε βρέθηκε. " +
                        "Ας προσπαθήσουμε ξανά, πιο προσεκτικά!");
                    return await stepContext.ReplaceDialogAsync(InitialDialogId, new CredentialsOptions(), cancellationToken);
                }
            }

            credentialsOptions.PhoneOwnerUserId = phoneOwner.Id;

            var ownerLogins = phoneOwner.AspNetUserLogins?.
                Where(l => l.LoginProvider == activity.ChannelId.ToLoginProvider().GetProviderName());
            
            bool isOwnerLoggedIn = ownerLogins != null && ownerLogins.Any(l => l.IsActive);
            bool hasAffiliatedUsers = userRepository.AnyAffiliatedUsers(phoneOwner.Id);

            if (hasAffiliatedUsers)
            {
                if (isOwnerLoggedIn)
                {
                    return await stepContext.PromptAsync(
                        nameof(UnaccentedChoicePrompt),
                        new PromptOptions
                        {
                            Prompt = MessageFactory.Text("Ποιος πρόκειται να συνδεθεί;"),
                            RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω επιλογές:"),
                            Choices = ChoiceFactory.ToChoices(new[] { "Γονέας", "Μαθητής" })
                        });
                }
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> VerificationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var credentialsOptions = stepContext.Options as CredentialsOptions;
            credentialsOptions.IsOwnerAuthentication = 
                stepContext.Result is not FoundChoice foundChoice || foundChoice.Index == 0;
            
            if (credentialsOptions.IsOwnerAuthentication)
            {
                //TODO: Log out the owner from other logins of this provider ?

                credentialsOptions.VerifiedUserId = credentialsOptions.PhoneOwnerUserId;

                await stepContext.Context.SendActivityAsync("Εντάξει, ας συνεχίσουμε!");
                return await stepContext.BeginDialogAsync(nameof(VerificationDialog),
                    new VerificationOptions(credentialsOptions), cancellationToken);
            }

            var affiliatedUsers = userRepository.
                FindChildren(credentialsOptions.PhoneOwnerUserId).
                Where(u => !u.User.IsSelfDetermined);

            foreach (var affUser in affiliatedUsers)
            {
                string code = affUser.User.IdentifierCode;
                var codeCreatedAt = affUser.User.IdentifierCodeCreatedAt;

                if (!string.IsNullOrEmpty(code) && codeCreatedAt.HasValue && 
                    !CredentialsOptions.IsCodeExpired(codeCreatedAt.Value))
                {
                    credentialsOptions.Codes.Add(code, affUser.Id);
                    credentialsOptions.CodesCreatedAt.Add(code, affUser.User.IdentifierCodeCreatedAt.Value);
                }
            }

            if (!credentialsOptions.Codes.Any())
            {
                await stepContext.Context.SendActivityAsync("Δεν υπάρχουν ενεργοί κωδικοί επαλήθευσης προς το παρόν.");
                await stepContext.Context.SendActivityAsync("Ζήτησε από τον γονέα σου να δημιουργήσει νέους " +
                    "στην κατηγορία «🗝 Πρόσβαση» και προσπάθησε ξανά.");

                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            return await stepContext.BeginDialogAsync(nameof(VerificationDialog),
                new VerificationOptions(credentialsOptions), cancellationToken);
        }

        private async Task<DialogTurnResult> PostVerificationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var credentialsOptions = stepContext.Options as CredentialsOptions;
            if (stepContext.Result is int verifiedUserId)
            {
                credentialsOptions.VerifiedUserId = verifiedUserId;
                
                // Remove user's code if any (single use only)
                var user = await userRepository.Find(verifiedUserId);
                user.User.IdentifierCode = null;
                user.User.IdentifierCodeCreatedAt = null;

                userRepository.Update(user);
            }

            return await stepContext.EndDialogAsync(credentialsOptions, cancellationToken);
        }

        #endregion
    }
}
