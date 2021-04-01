using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.DataHandle.Repositories;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.DataHandle.Main;
using System;
using Phoenix.Bot.Utilities.State.Options.Authentification;

namespace Phoenix.Bot.Dialogs.Authentication
{
    public class CredentialsDialog : ComponentDialog
    {
        private readonly IConfiguration configuration;
        private readonly AspNetUserRepository userRepository;

        public CredentialsDialog(IConfiguration configuration, PhoenixContext phoenixContext,
            VerificationDialog verificationDialog)
            : base(nameof(CredentialsDialog))
        {
            this.configuration = configuration;
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
                    ConfirmationStepAsync,
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
            bool isAssignedToCurrentSchool = phoneOwner.UserSchool?.Any(us => us.School.FacebookPageId == activity.Recipient.Id) ?? false;

            if (phoneOwner is null || !isAssignedToCurrentSchool)
            {
                await stepContext.Context.SendActivityAsync("Το κινητό τηλέφωνο δε βρέθηκε. Ας προσπαθήσουμε ξανά, πιο προσεκτικά!");
                return await stepContext.ReplaceDialogAsync(InitialDialogId, new CredentialsOptions(), cancellationToken);
            }

            credentialsOptions.PhoneOwnerUserId = phoneOwner.Id;

            var ownerLogins = phoneOwner.AspNetUserLogins?.
                Where(l => l.LoginProvider == activity.ChannelId.ToLoginProvider().GetProviderName());

            //False means that it is unknown whom the current user is (it could be the owner)
            bool providerKeyBelongsToOwner = ownerLogins?.Any(l => l.ProviderKey == activity.From.Id) ?? false;

            //Verification of the owner
            if (ownerLogins is null || !ownerLogins.Any() || ownerLogins.All(l => !l.IsActive) || providerKeyBelongsToOwner)
            {
                credentialsOptions.IsOwnerAuthentication = true;
                credentialsOptions.VerifiedUserId = phoneOwner.Id;

                if (userRepository.AnyAffiliatedUsers(phoneOwner.Id) && !providerKeyBelongsToOwner)
                {
                    await stepContext.Context.SendActivityAsync("Υπενθυμίζεται πως η πρώτη σύνδεση πρέπει να γίνει από τον ιδιοκτήτη του αριθμού.");
                    return await stepContext.PromptAsync(nameof(UnaccentedChoicePrompt), 
                        new YesNoPromptOptions("Ο αριθμός ανήκει σε εμένα και επιθυμώ να συνεχίσω:", simpleNo: true), cancellationToken);
                }
                
                return await stepContext.BeginDialogAsync(nameof(VerificationDialog), new VerificationOptions(credentialsOptions), cancellationToken);
            }

            //Verify another member
            var affiliatedUsers = userRepository.FindChildren(phoneOwner.Id).Where(u => !u.User.IsSelfDetermined);
            if (!affiliatedUsers.Any())
            {
                await stepContext.Context.SendActivityAsync("Ο αριθμός αυτός δεν έχει συσχετιστεί με άλλους χρήστες.");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            foreach (var affUser in affiliatedUsers)
            {
                string code = affUser.User.IdentifierCode;
                var codeCreatedAt = affUser.User.IdentifierCodeCreatedAt;
                if (!string.IsNullOrEmpty(code) && codeCreatedAt.HasValue && !CredentialsOptions.IsCodeExpired(codeCreatedAt.Value))
                {
                    credentialsOptions.Codes.Add(code, affUser.Id);
                    credentialsOptions.CodesCreatedAt.Add(code, affUser.User.IdentifierCodeCreatedAt.Value);
                }
            }
            
            if (!credentialsOptions.Codes.Any())
            {
                await stepContext.Context.SendActivityAsync("Δεν υπάρχουν ενεργοί κωδικοί επαλήθευσης προς το παρόν.");
                await stepContext.Context.SendActivityAsync("Επικοινωνήστε με τον ιδιοκτήτη του αριθμού " +
                    "ώστε να δημιουργήσει έναν κωδικό για να συνδεθείτε και προσπαθήστε ξανά.");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            credentialsOptions.IsOwnerAuthentication = false;
            return await stepContext.BeginDialogAsync(nameof(VerificationDialog), new VerificationOptions(credentialsOptions), cancellationToken);
        }

        private async Task<DialogTurnResult> ConfirmationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is not FoundChoice foundChoice)
                return await stepContext.NextAsync(stepContext.Result, cancellationToken);

            var credentialsOptions = stepContext.Options as CredentialsOptions;

            if (foundChoice.Index == 0)
            {
                await stepContext.Context.SendActivityAsync("Εντάξει, ας συνεχίσουμε!");
                return await stepContext.BeginDialogAsync(nameof(VerificationDialog), new VerificationOptions(credentialsOptions), cancellationToken);
            }
            
            await stepContext.Context.SendActivityAsync("Παρακαλώ συνδεθείτε από τον λογαρισμό του ιδιοκτήτη του αριθμού, ώστε να ενεργοποιηθούν" +
                " οι συνδέσεις των υπόλοιπων μελών.");

            credentialsOptions.IsOwnerAuthentication = false;
            credentialsOptions.VerifiedUserId = null;

            return await stepContext.EndDialogAsync(null, cancellationToken);
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
