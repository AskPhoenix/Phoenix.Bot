using Microsoft.Bot.Schema;
using Phoenix.Bot.Dialogs.Actions;
using Phoenix.Bot.Dialogs.Auth;
using Phoenix.Bot.Utilities.State.Options.Actions;

namespace Phoenix.Bot.Dialogs
{
    public class IntroductionDialog : StateDialog
    {
        public IntroductionDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext,
            AuthenticationDialog authDialog,
            HelpDialog helpDialog)
            : base(userState, convState, userManager, phoenixContext, nameof(IntroductionDialog))
        {
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

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            await stepCtx.Context.SendActivityAsync("Καλωσόρισες στον έξυπνο βοηθό μας! 😁");

            var card = new HeroCard
            {
                Title = CData.School.Name,
                Text = "Πάτησε ή πληκτρολόγησε \"Σύνδεση\" για να ξεκινήσουμε!",
                Tap = new(ActionTypes.OpenUrl, value: "https://www.askphoenix.gr"),
                Buttons = new List<CardAction>
                {
                    new(ActionTypes.ImBack, title: "🔓 Σύνδεση", value: "🔓 Σύνδεση"),
                    new(ActionTypes.OpenUrl, title: "🦜 Περισσότερα...", value: "https://www.askphoenix.gr")
                }
            };

            var reply = (Activity)MessageFactory.Attachment(card.ToAttachment());
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Choices = new Choice[] { new("🔓 Σύνδεση") },
                    Style = ListStyle.None
                }, canTkn);
        }

        private async Task<DialogTurnResult> TermsStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var card = new HeroCard
            {
                Title = "Όροι Παροχής Υπηρεσιών",
                Text = "Πριν ξεκινήσουμε θα πρέπει να διαβάσεις και να αποδεχθείς τους όρους χρήσης.",
                Tap = new(ActionTypes.OpenUrl, value: "https://bot.askphoenix.gr/legal/terms-conditions.html"),
                Buttons = new List<CardAction>
                {
                    new(ActionTypes.ImBack, title: "✔️ Συμφωνώ", value: "✔️ Συμφωνώ"),
                    new(ActionTypes.ImBack, title: "❌ Διαφωνώ", value: "❌ Διαφωνώ"),
                    new(ActionTypes.OpenUrl, title: "📖 Ανάγνωση...", value: "https://bot.askphoenix.gr/legal/terms-conditions.html")
                }
            };

            var reply = (Activity)MessageFactory.Attachment(card.ToAttachment());
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Choices = new Choice[] { new("✔️ Συμφωνώ"), new("❌ Διαφωνώ") },
                    Style = ListStyle.None
                }, canTkn);
        }

        private async Task<DialogTurnResult> TermsReplyStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            if (foundChoice.Index == 1)
                throw new BotException(BotError.UserNotAcceptedTerms, showMessageOnly: true);
               
            await stepCtx.Context.SendActivityAsync("Τέλεια! Τώρα μπορούμε να συνεχίσουμε με τη σύνδεσή σου! 😁");
            return await stepCtx.BeginDialogAsync(
                nameof(AuthenticationDialog), new AuthenticationOptions(), canTkn);
        }

        private async Task<DialogTurnResult> WelcomeAskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken cancTkn)
        {
            if ((bool)stepCtx.Result)
            {
                HelpOptions helpOptions = new()
                {
                    AskForTutorial = true
                };

                return await stepCtx.BeginDialogAsync(nameof(HelpDialog), helpOptions, cancTkn);
            }

            return await stepCtx.EndDialogAsync(false, cancTkn);
        }

        private async Task<DialogTurnResult> EndStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.EndDialogAsync(true, canTkn);
        }

        #endregion
    }
}
