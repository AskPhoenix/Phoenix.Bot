using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Dialogs.Actions;
using Phoenix.Bot.Dialogs.Authentication;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;

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

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            await stepCtx.Context.SendActivityAsync("Καλωσόρισες στον έξυπνο βοηθό μας! 😁");

            var card = new HeroCard
            {
                Title = UData.School.Name,
                Text = "Πάτησε ή πληκτρολόγησε \"Σύνδεση\" για να ξεκινήσουμε!",
                Tap = new CardAction(ActionTypes.OpenUrl, value: "https://www.askphoenix.gr"),
                Buttons = new List<CardAction>
                {
                    new CardAction(ActionTypes.ImBack, title: "🔓 Σύνδεση", value: "🔓 Σύνδεση"),
                    new CardAction(ActionTypes.OpenUrl, title: "🦜 Περισσότερα...", value: "https://www.askphoenix.gr")
                }
            };

            var reply = (Activity)MessageFactory.Attachment(card.ToAttachment());
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Choices = new Choice[] { new Choice("🔓 Σύνδεση") },
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
                Tap = new CardAction(ActionTypes.OpenUrl, value: "https://bot.askphoenix.gr/legal/terms-conditions.html"),
                Buttons = new List<CardAction>
                {
                    new CardAction(ActionTypes.ImBack, title: "✔️ Συμφωνώ", value: "✔️ Συμφωνώ"),
                    new CardAction(ActionTypes.ImBack, title: "❌ Διαφωνώ", value: "❌ Διαφωνώ"),
                    new CardAction(ActionTypes.OpenUrl, title: "📖 Ανάγνωση...", value: "https://bot.askphoenix.gr/legal/terms-conditions.html")
                }
            };

            var reply = (Activity)MessageFactory.Attachment(card.ToAttachment());
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = reply,
                    Choices = new Choice[] { new Choice("✔️ Συμφωνώ"), new Choice("❌ Διαφωνώ") },
                    Style = ListStyle.None
                }, canTkn);
        }

        private async Task<DialogTurnResult> TermsReplyStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            if (foundChoice.Index == 1)
            {
                await stepCtx.Context.SendActivityAsync("Θα πρέπει πρώτα να αποδεχθείς τους όρους χρήσης για να ξεκινήσουμε.");
                return await stepCtx.EndDialogAsync(false, canTkn);
            }
                
            await stepCtx.Context.SendActivityAsync("Τέλεια! Τώρα μπορούμε να συνεχίσουμε με τη σύνδεσή σου! 😁");
            return await stepCtx.BeginDialogAsync(nameof(AuthenticationDialog), null, canTkn);
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
    }
}
