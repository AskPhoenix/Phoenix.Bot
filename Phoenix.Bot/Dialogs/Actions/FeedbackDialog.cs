using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs.Prompts.Options;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Main.Types;
using Phoenix.DataHandle.Repositories;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class FeedbackDialog : StateDialog
    {
        private readonly BotFeedbackRepository _feedbackRepository;

        public FeedbackDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, nameof(FeedbackDialog))
        {
            _feedbackRepository = new(phoenixContext);

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

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options,
            CancellationToken canTkn = default)
        {
            var feedbackOptions = (FeedbackOptions)options;

            if (feedbackOptions.BotAskedForFeedback)
                InitialDialogId = WaterfallNames.Feedback.Ask;

            return base.OnBeginDialogAsync(innerDc, options, canTkn);
        }

        #region Ask Waterfall Dialog

        private async Task<DialogTurnResult> AskForFeedbackStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να κάνεις ένα σχόλιο για τη μέχρι τώρα εμπειρία σου στον ψηφιακό μας βοηθό; 😊"),
                canTkn);
        }

        private async Task<DialogTurnResult> ReplyFeedbackStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;

            if (foundChoice.Index == 0)
                return await stepCtx.ReplaceDialogAsync(WaterfallNames.Feedback.Top, stepCtx.Options, canTkn);

            await stepCtx.Context.SendActivityAsync("Εντάξει! Ίσως μια άλλη φορά!");

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> TypeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            await stepCtx.Context.SendActivityAsync("Τα σχόλιά σου είναι πολύτιμα για να γίνουμε ακόμα καλύτεροι! 😁");
            
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Τι είδους σχόλιο θα ήθελες να κάνεις;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω κατηγορίες:"),
                    Choices = ChoiceFactory.ToChoices(BotFeedbackCategoryExtensions.GetEmojiStrings())
                }, canTkn);
        }

        private async Task<DialogTurnResult> RedirectStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;
            var feedbackCat = (BotFeedbackCategory)(foundChoice.Index + 1);

            stepCtx.Values.Add(nameof(BotFeedbackCategory), feedbackCat);

            if (feedbackCat == BotFeedbackCategory.Rating)
                return await stepCtx.BeginDialogAsync(WaterfallNames.Feedback.Rating, null, canTkn);
            
            return await stepCtx.BeginDialogAsync(WaterfallNames.Feedback.Comment, feedbackCat, canTkn);
        }

        private async Task<DialogTurnResult> SaveStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var feedbackOptions = (FeedbackOptions)stepCtx.Options;

            var botFeedback = new BotFeedback()
            {
                AuthorId = UData.PhoenixUser!.AspNetUserId,
                AskTriggered = feedbackOptions.BotAskedForFeedback,
                Category = (BotFeedbackCategory)stepCtx.Values[nameof(BotFeedbackCategory)]
            };

            if (stepCtx.Result is byte rating)
                botFeedback.Rating = rating;
            else if (stepCtx.Result is string comment)
                botFeedback.Comment = comment;

            await _feedbackRepository.CreateAsync(botFeedback);

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Rating Waterfall Dialog

        private async Task<DialogTurnResult> RatingPromptStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πώς με βαθμολογείς;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ένα από τα παρακάτω εικονίδια:"),
                    Choices = ChoiceFactory.ToChoices(new[] { "😒", "😐", "🙂", "😄", "😍" })
                }, canTkn);
        }

        private async Task<DialogTurnResult> RatingReplyStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)stepCtx.Result;
            var rating = (byte)(foundChoice.Index + 1);

            await stepCtx.Context.SendActivityAsync("Σ'ευχαριστώ πολύ για τη βαθμολογία σου! 😊");
            return await stepCtx.EndDialogAsync(rating, canTkn);
        }

        #endregion

        #region Comment Waterfall Dialog

        private async Task<DialogTurnResult> CommentPromptStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            string reply = (BotFeedbackCategory)stepCtx.Options switch
            {
                BotFeedbackCategory.Compliment  => "Τέλεια!! 😍 Ανυπομονώ να ακούσω:",
                BotFeedbackCategory.Suggestion  => "Ανυπομονώ να ακούσω την ιδέα σου:",
                BotFeedbackCategory.Complaint   => "Λυπάμαι αν σε στενοχώρησα 😢 Πες μου τι σε ενόχλησε:",
                _                               => "Ωραία! Σε ακούω:"
            };

            return await stepCtx.PromptAsync(
                nameof(TextPrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text(reply),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ γράψε το σχόλιό σου παρακάτω:")
                }, canTkn);
        }

        private async Task<DialogTurnResult> CommentReplyStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var comment = (string)stepCtx.Result;

            await stepCtx.Context.SendActivityAsync("Σ' ευχαριστώ πολύ για το σχόλιό σου! 😊");
            return await stepCtx.EndDialogAsync(comment, canTkn);
        }

        #endregion
    }
}
