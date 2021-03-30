using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Dialogs.Actions;
using Phoenix.Bot.Dialogs.Authentication;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Miscellaneous;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs
{
    public class IntroductionDialog : ComponentDialog
    {
        private readonly SchoolRepository schoolRepository;
        private readonly AspNetUserRepository userRepository;

        public IntroductionDialog(PhoenixContext phoenixContext,
            AuthenticationDialog authDialog, HelpDialog helpDialog)
            : base(nameof(IntroductionDialog))
        {
            this.schoolRepository = new SchoolRepository(phoenixContext);
            this.userRepository = new AspNetUserRepository(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(authDialog);
            AddDialog(helpDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Introduction.Top,
                new WaterfallStep[]
                {
                    IntroStepAsync,
                    TermsStepAsync,
                    TermsReplyStepAsync,
                    WelcomeAskStepAsync,
                    EndStepAsync
                }));

            InitialDialogId = WaterfallNames.Introduction.Top;
        }

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string schoolName = (await this.schoolRepository.Find(s => s.FacebookPageId == stepContext.Context.Activity.Recipient.Id))?.Name;

            await stepContext.Context.SendActivityAsync("Καλωσόρισες στον έξυπνο βοηθό μας! 😁");
            var card = new HeroCard
            {
                Title = schoolName ?? "AskPhoenix",
                Text = "Πάτησε ή πληκτρολόγησε \"Σύνδεση\" για να ξεκινήσουμε!",
                Tap = new CardAction(ActionTypes.OpenUrl, value: "https://www.askphoenix.gr"),
                Buttons = new List<CardAction>
                {
                    new CardAction(ActionTypes.ImBack, title: "🔓 Σύνδεση", value: "🔓 Σύνδεση"),
                    new CardAction(ActionTypes.OpenUrl, title: "🦜 Περισσότερα...", value: "https://www.askphoenix.gr")
                }
            };

            var reply = (Activity)MessageFactory.Attachment(card.ToAttachment());
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Choices = new Choice[] { new Choice("🔓 Σύνδεση") },
                    Style = ListStyle.None
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> TermsStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var provider = stepContext.Context.Activity.ChannelId.ToLoginProvider();
            var providerKey = stepContext.Context.Activity.From.Id;

            var user = userRepository.FindUserFromLogin(provider, providerKey);
            if (user != null && user.User.TermsAccepted)
                await stepContext.NextAsync(null, cancellationToken);

            var card = new HeroCard
            {
                Title = "Όροι Παροχής Υπηρεσίας",
                Text = "Πριν ξεκινήσουμε θα πρέπει να διαβάσεις και να αποδεχθείς τους όρους χρήσης.",
                Tap = new CardAction(ActionTypes.OpenUrl, value: "https://www.bot.askphoenix.gr/legal/terms-conditions.html"),
                Buttons = new List<CardAction>
                {
                    new CardAction(ActionTypes.ImBack, title: "✔️ Συμφωνώ", value: "✔️ Συμφωνώ"),
                    new CardAction(ActionTypes.ImBack, title: "❌ Διαφωνώ", value: "❌ Διαφωνώ"),
                    new CardAction(ActionTypes.OpenUrl, title: "📖 Ανάγνωση...", value: "https://www.bot.askphoenix.gr/legal/terms-conditions.html")
                }
            };

            var reply = (Activity)MessageFactory.Attachment(card.ToAttachment());
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Choices = new Choice[] { new Choice("✔️ Συμφωνώ"), new Choice("❌ Διαφωνώ") },
                    Style = ListStyle.None
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> TermsReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is FoundChoice foundChoice && foundChoice.Index == 1)
            {
                if (foundChoice.Index == 1)
                {
                    await stepContext.Context.SendActivityAsync("Θα πρέπει πρώτα να αποδεχθείς τους όρους χρήσης για να ξεκινήσουμε.");
                    return await stepContext.EndDialogAsync(false, cancellationToken);
                }
                
                await stepContext.Context.SendActivityAsync("Τέλεια! Τώρα μπορούμε να συνεχίσουμε με τη σύνδεσή σου! 😁");
            }

            return await stepContext.BeginDialogAsync(nameof(AuthenticationDialog), null, cancellationToken);
        }

        private async Task<DialogTurnResult> WelcomeAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            bool authResult = (bool)stepContext.Result;
            if (authResult)
                return await stepContext.BeginDialogAsync(nameof(HelpDialog), new HelpOptions() { AskForTutorial = true }, cancellationToken);

            return await stepContext.EndDialogAsync(false, cancellationToken);
        }

        private async Task<DialogTurnResult> EndStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.EndDialogAsync(true, cancellationToken);
        }
    }
}
