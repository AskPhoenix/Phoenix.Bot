using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class FeedbackDialog : ComponentDialog
    {
        private readonly Repository<BotFeedback> feedbackRepository;

        public FeedbackDialog(PhoenixContext phoenixContext)
            : base(nameof(FeedbackDialog))
        {
            this.feedbackRepository = new Repository<BotFeedback>(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Feedback.Ask,
                new WaterfallStep[]
                {
                    AskForFeedbackStepAsync,
                    ReplyFeedbackStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Feedback.Top,
                new WaterfallStep[] 
                {
                    TypeStepAsync,
                    RedirectStepAsync,
                    SaveStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Feedback.Rating,
                new WaterfallStep[]
                {
                    RatingPromptStepAsync,
                    RatingReplyStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Feedback.Comment,
                new WaterfallStep[]
                {
                    CommentPromptStepAsync,
                    CommentReplyStepAsync
                }));

            InitialDialogId = WaterfallNames.Feedback.Top;
        }

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            var feedbackOptions = options as FeedbackOptions;
            if (feedbackOptions.BotAskedForFeedback)
                InitialDialogId = WaterfallNames.Feedback.Ask;

            return base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        #region Ask Waterfall Dialog

        private async Task<DialogTurnResult> AskForFeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να κάνεις ένα σχόλιο για τη μέχρι τώρα εμπειρία σου στο Phoenix; 😊"),
                cancellationToken);
        }

        private async Task<DialogTurnResult> ReplyFeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Feedback.Top, stepContext.Options, cancellationToken);

            await stepContext.Context.SendActivityAsync("Εντάξει! Ίσως μια άλλη φορά!");
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> TypeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Τα σχόλιά σου είναι πολύτιμα για να γίνει το Phoenix ακόμα καλύτερο! 😁");
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Τι είδους σχόλιο θα ήθελες να κάνεις;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω κατηγορίες:"),
                    Choices = ChoiceFactory.ToChoices(BotFeedbackTypeExtensions.GetAllGreekNames(positiveOnly: true, includeEmoji: true).ToList())
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> RedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var type = (BotFeedbackType)((stepContext.Result as FoundChoice).Index + 1);
            stepContext.Values.Add("type", type.ToFriendlyString());

            if (type == BotFeedbackType.Rating)
                return await stepContext.BeginDialogAsync(WaterfallNames.Feedback.Rating, null, cancellationToken);
            
            return await stepContext.BeginDialogAsync(WaterfallNames.Feedback.Comment, type, cancellationToken);
        }

        private async Task<DialogTurnResult> SaveStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var feedbackOptions = stepContext.Options as FeedbackOptions;
            BotFeedback botFeedback = new BotFeedback()
            {
                AuthorId = feedbackOptions.UserId,
                AskTriggered = feedbackOptions.BotAskedForFeedback,
                Type = (string)stepContext.Values["type"]
            };

            if (stepContext.Result is byte ratingResult)
                botFeedback.Rating = ratingResult;
            else if (stepContext.Result is string commentResult)
                botFeedback.Comment = commentResult;

            feedbackRepository.Create(botFeedback);

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Rating Waterfall Dialog

        private async Task<DialogTurnResult> RatingPromptStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πώς με βαθμολογείς;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ένα από τα παρακάτω εικονίδια:"),
                    Choices = ChoiceFactory.ToChoices(new string[] { "😍", "😄", "🙂", "😐", "😒" })
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> RatingReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            byte rating = (byte)(5 - (stepContext.Result as FoundChoice).Index);

            await stepContext.Context.SendActivityAsync("Σ' ευχαριστώ πολύ για τη βαθμολογία σου! 😊");
            return await stepContext.EndDialogAsync(rating, cancellationToken);
        }

        #endregion

        #region Comment Waterfall Dialog

        private async Task<DialogTurnResult> CommentPromptStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string reply = (BotFeedbackType)stepContext.Options switch
            {
                BotFeedbackType.Copliment   => "Τέλεια!! 😍 Ανυπομονώ να ακούσω:",
                BotFeedbackType.Suggestion  => "Ανυπομονώ να ακούσω την ιδέα σου:",
                BotFeedbackType.Complaint   => "Λυπάμαι αν σε στενοχώρησα 😢 Πες μου τι σε ενόχλησε:",
                _                               => "Ωραία! Σε ακούω:"
            };

            return await stepContext.PromptAsync(
                nameof(TextPrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text(reply),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ γράψε το σχόλιό σου παρακάτω:")
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> CommentReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var comment = (string)stepContext.Result;

            await stepContext.Context.SendActivityAsync("Σ' ευχαριστώ πολύ για το σχόλιό σου! 😊");
            return await stepContext.EndDialogAsync(comment, cancellationToken);
        }

        #endregion
    }
}
