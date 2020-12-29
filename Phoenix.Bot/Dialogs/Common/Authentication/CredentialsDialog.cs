﻿using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using System.Threading;
using System.Threading.Tasks;
using Phoenix.DataHandle.Main.Models;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.DialogOptions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.DataHandle.Repositories;
using Microsoft.Bot.Schema;
using Microsoft.Bot.Builder.Dialogs.Choices;

namespace Phoenix.Bot.Dialogs.Common.Authentication
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
            => await stepContext.PromptAsync(
                PromptNames.Phone,
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Παρακαλώ πληκτρολόγησε τον αριθμό παρακάτω:"),
                    RetryPrompt = MessageFactory.Text("Ο αριθμός τηλεφώνου πρέπει να είναι στη μορφή 69xxxxxxxx. Παρακαλώ πληκτρολόγησέ τον ξανά:")
                });

        private async Task<DialogTurnResult> EligibilityStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            Activity activity = stepContext.Context.Activity;
            string phone = stepContext.Result.ToString();

            var authenticationOptions = stepContext.Options as AuthenticationOptions;
            authenticationOptions.Phone = phone;

            var phoneUsers = userRepository.Find().
                Include(u => u.User).
                Include(u => u.AspNetUserLogins).
                Where(u => u.PhoneNumber == phone)?.
                Where(u => u.UserSchool.Any(us => us.School.FacebookPageId == activity.Recipient.Id));

            var phoneOwner = phoneUsers?.
                SingleOrDefault(u => u.User.IsSelfDetermined);

            if (phoneOwner == default)
            {
                await stepContext.Context.SendActivityAsync("Το κινητό τηλέφωνο δε βρέθηκε. Ας προσπαθήσουμε ξανά, πιο προσεκτικά!");
                return await stepContext.ReplaceDialogAsync(InitialDialogId, new AuthenticationOptions(), cancellationToken);
            }

            var ownerLogins = phoneOwner.AspNetUserLogins?.
                Where(l => l.LoginProvider == activity.ChannelId.ToUpper());

            //False means that it is unknown whom the current user is (it could be the owner)
            bool providerKeyBelongsToOwner = ownerLogins?.Any(l => l.ProviderKey == activity.From.Id) ?? false;

            //Verification of the owner
            if (ownerLogins == default || ownerLogins.All(l => !l.IsActive) || providerKeyBelongsToOwner)
            {
                if (phoneUsers.Count() > 1 && !providerKeyBelongsToOwner)
                {
                    await stepContext.Context.SendActivityAsync("Υπενθυμίζεται πως η πρώτη σύνδεση πρέπει να γίνει από τον γονέα.");
                    return await stepContext.PromptAsync(nameof(UnaccentedChoicePrompt), 
                        new YesNoPromptOptions("Είμαι γονέας και επιθυμώ να συνεχίσω:"), cancellationToken);
                }

                authenticationOptions.IsOwnerVerification = true;
                return await stepContext.BeginDialogAsync(nameof(VerificationDialog), authenticationOptions, cancellationToken);
            }

            //Verify another member
            var phoneMember = phoneUsers.
                SingleOrDefault(u => u.AspNetUserLogins.Any(l => l.LoginProvider == activity.ChannelId.ToUpper() && l.ProviderKey == activity.From.Id));

            authenticationOptions.Codes = phoneUsers.Where(u => !u.User.IsSelfDetermined).Select(u => u.User.IdentifierCode).ToArray();
            authenticationOptions.IsOwnerVerification = false;
            return await stepContext.BeginDialogAsync(nameof(VerificationDialog), authenticationOptions, cancellationToken);
        }

        private async Task<DialogTurnResult> ConfirmationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is AuthenticationOptions)
                return await stepContext.NextAsync(stepContext.Result, cancellationToken);

            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
            {
                await stepContext.Context.SendActivityAsync("Εντάξει, ας συνεχίσουμε!");

                var verificationOptions = new AuthenticationOptions()
                {
                    IsOwnerVerification = true,
                    Phone = (stepContext.Options as AuthenticationOptions).Phone
                };
                return await stepContext.BeginDialogAsync(nameof(VerificationDialog), verificationOptions, cancellationToken);
            }
            
            await stepContext.Context.SendActivityAsync("Παρακαλώ συνδεθείτε από τον λογαρισμό του γονέα για να ενεργοποιηθούν οι συνδέσεις για τους μαθητές.");
            return await stepContext.EndDialogAsync(false, cancellationToken);
        }

        private async Task<DialogTurnResult> PostVerificationStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.EndDialogAsync((stepContext.Result as AuthenticationOptions).Verified, cancellationToken);
        }

        #endregion
    }
}
