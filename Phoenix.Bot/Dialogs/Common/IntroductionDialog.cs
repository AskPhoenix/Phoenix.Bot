using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Dialogs.Common.Authentication;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Common
{
    public class IntroductionDialog : ComponentDialog
    {
        private readonly SchoolRepository schoolRepository;
        private readonly IStatePropertyAccessor<UserData> userDataAccesor;
        private readonly IStatePropertyAccessor<MainState> mainStateAccesor;

        public IntroductionDialog(PhoenixContext phoenixContext, UserState userState, ConversationState conversationState,
            AuthDialog authDialog, HelpDialog welcomeDialog)
            : base(nameof(IntroductionDialog))
        {
            this.schoolRepository = new SchoolRepository(phoenixContext);
            this.userDataAccesor = userState.CreateProperty<UserData>(nameof(UserData));
            this.mainStateAccesor = conversationState.CreateProperty<MainState>(nameof(MainState));

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(authDialog);
            AddDialog(welcomeDialog);

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
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 1)
            {
                await stepContext.Context.SendActivityAsync("Λυπάμαι, αλλά θα πρέπει πρώτα να αποδεχθείς τους όρους χρήσης " +
                    "ώστε να χρησιμοποιήσεις τις υπηρεσίες του AskPhoenix.");
                return await stepContext.EndDialogAsync(false, cancellationToken);
            }

            var userData = await userDataAccesor.GetAsync(stepContext.Context, cancellationToken: cancellationToken);
            userData.HasAcceptedTerms = true;
            await userDataAccesor.SetAsync(stepContext.Context, userData, cancellationToken);

            await stepContext.Context.SendActivityAsync("Τέλεια! Τώρα μπορούμε να συνεχίσουμε με τη σύνδεσή σου! 😁");
            return await stepContext.BeginDialogAsync(nameof(AuthDialog), null, cancellationToken);
        }

        private async Task<DialogTurnResult> WelcomeAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var mainState = await mainStateAccesor.GetAsync(stepContext.Context, null, cancellationToken);
            mainState.UserWelcomed = true;
            await mainStateAccesor.SetAsync(stepContext.Context, mainState, cancellationToken);

            return await stepContext.BeginDialogAsync(nameof(HelpDialog), new HelpOptions() { AskForTutorial = true }, cancellationToken);
        }

        private async Task<DialogTurnResult> EndStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }
    }
}
