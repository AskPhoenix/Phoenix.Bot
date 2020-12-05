using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.EntityFrameworkCore;
using Phoenix.Bot.Utilities.Channels.Facebook;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8509

namespace Phoenix.Bot.Dialogs.Common
{
    public class FeedbackDialog : ComponentDialog
    {
        private readonly PhoenixContext _phoenixContext;
        private readonly BotState _conversationState;

        private readonly IStatePropertyAccessor<BotFeedback> _botFeedback;

        private static class WaterfallNames
        {
            public const string Spontaneous = "FeedbackSpontaneous_WaterfallDialog";
            public const string Triggered   = "FeedbackTriggered_WaterfallDialog";
            public const string Comment     = "FeedbackComment_WaterfallDialog";
            public const string Rating      = "FeedbackRating_WaterfallDialog";
        }

        public FeedbackDialog(PhoenixContext phoenixContext, ConversationState conversationState)
            : base(nameof(FeedbackDialog))
        {
            _phoenixContext = phoenixContext;
            _conversationState = conversationState;
            _botFeedback = _conversationState.CreateProperty<BotFeedback>("BotFeedback");

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new TextPrompt(nameof(TextPrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Spontaneous,
                new WaterfallStep[] 
                {
                    CategoryStepAsync,
                    RedirectStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Triggered,
                new WaterfallStep[]
                {
                    AskForFeedbackStepAsync,
                    ReplyFeedbackStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Rating,
                new WaterfallStep[]
                {
                    RatingPromptStepAsync,
                    RatingReplyStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Comment,
                new WaterfallStep[]
                {
                    CommentPromptStepAsync,
                    CommentReplyStepAsync
                }));
        }

        protected override async Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options, CancellationToken cancellationToken = default)
        {
            var botFeedback = new BotFeedback()
            {
                AuthorId = _phoenixContext.AspNetUserLogins.
                    Include(l => l.User).
                    Single(l => l.LoginProvider == LoginProvider.Facebook.GetProviderName() && l.ProviderKey == innerDc.Context.Activity.From.Id).
                    User.
                    Id
            };

            if (Persistent.TryGetCommand(innerDc.Context.Activity.Text, out Persistent.Command cmd) && cmd == Persistent.Command.Feedback)
            {
                botFeedback.Occasion = BotFeedbackOccasion.Persistent_Menu;
                InitialDialogId = WaterfallNames.Spontaneous;
            }
            else
            {
                botFeedback.Occasion = (BotFeedbackOccasion)options;
                InitialDialogId = WaterfallNames.Triggered;
            }

            await _botFeedback.SetAsync(innerDc.Context, botFeedback);

            return await base.OnBeginDialogAsync(innerDc, options, cancellationToken);
        }

        protected override Task OnEndDialogAsync(ITurnContext context, DialogInstance instance, DialogReason reason, CancellationToken cancellationToken = default)
        {
            var botFeedback = Task.Run<BotFeedback>(async () => 
            {
                var botFeedback = await _botFeedback.GetAsync(context);
                await _botFeedback.DeleteAsync(context);
                return botFeedback;
            }).Result;

            if (botFeedback.Comment != null || botFeedback.Rating != null)
            {
                _phoenixContext.Add(botFeedback);
                _phoenixContext.SaveChanges();
            }

            return base.OnEndDialogAsync(context, instance, reason, cancellationToken);
        }

        #region Feedback Spontaneous Waterfall Dialog

        private async Task<DialogTurnResult> CategoryStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Τα σχόλιά σου είναι πολύτιμα για να γίνει το Phoenix ακόμα καλύτερο! 😁");
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Τι είδους σχόλιο θα ήθελες να κάνεις;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω κατηγορίες:"),
                    Choices = ChoiceFactory.ToChoices(BotFeedbackCategoryExtensions.GetAllGreekNames(includeEmoji: true).ToList())
                });
        }

        private async Task<DialogTurnResult> RedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var selCat = (BotFeedbackCategory)(stepContext.Result as FoundChoice).Index;

            var botFeedback = await _botFeedback.GetAsync(stepContext.Context);
            botFeedback.Category = selCat;
            await _botFeedback.SetAsync(stepContext.Context, botFeedback);

            if (selCat == BotFeedbackCategory.Rating)
                return await stepContext.BeginDialogAsync(WaterfallNames.Rating, null, cancellationToken);
            
            return await stepContext.BeginDialogAsync(WaterfallNames.Comment, selCat, cancellationToken);
        }

        #endregion

        #region Feedback Triggered Waterfall Dialog

        private async Task<DialogTurnResult> AskForFeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να κάνεις ένα σχόλιο για τη μέχρι τώρα εμπειρία σου στο Phoenix; 😊"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });
        }

        private async Task<DialogTurnResult> ReplyFeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var foundChoice = stepContext.Result as FoundChoice;
            if (foundChoice.Index == 0)
                return await stepContext.BeginDialogAsync(WaterfallNames.Comment, BotFeedbackCategory.Comment, cancellationToken);

            await stepContext.Context.SendActivityAsync("Εντάξει! Ίσως μια άλλη φορά!");
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Feedback Rating Waterfall Dialog

        private async Task<DialogTurnResult> RatingPromptStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πώς με βαθμολογείς;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ένα από τα παρακάτω εικονίδια:"),
                    Choices = ChoiceFactory.ToChoices(new string[] { "😍", "😄", "🙂", "😐", "😒" })
                });
        }

        private async Task<DialogTurnResult> RatingReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var botFeedback = await _botFeedback.GetAsync(stepContext.Context);
            botFeedback.Rating = (byte)(5 - (stepContext.Result as FoundChoice).Index);
            await _botFeedback.SetAsync(stepContext.Context, botFeedback);

            await stepContext.Context.SendActivityAsync("Σ' ευχαριστώ πολύ για τη βαθμολογία σου! 😊");
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion

        #region Feedback Comment Waterfall Dialog

        private async Task<DialogTurnResult> CommentPromptStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var reply = (BotFeedbackCategory)stepContext.Options switch
            {
                BotFeedbackCategory.Comment => MessageFactory.Text("Ωραία! Σε ακούω:"),
                BotFeedbackCategory.Copliment => MessageFactory.Text("Τέλεια!! 😍 Ανυπομονώ να ακούσω:"),
                BotFeedbackCategory.Suggestion => MessageFactory.Text("Ανυπομονώ να ακούσω την ιδέα σου:"),
                BotFeedbackCategory.Complaint => MessageFactory.Text("Λυπάμαι αν σε στενοχώρησα 😢 Πες μου τι σε ενόχλησε:")
            };

            return await stepContext.PromptAsync(
                nameof(TextPrompt),
                new PromptOptions
                {
                    Prompt = reply,
                    RetryPrompt = MessageFactory.Text("Παρακαλώ γράψε το σχόλιό σου παρακάτω:")
                });
        }

        private async Task<DialogTurnResult> CommentReplyStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var botFeedback = await _botFeedback.GetAsync(stepContext.Context);
            botFeedback.Comment = (string)stepContext.Result;
            await _botFeedback.SetAsync(stepContext.Context, botFeedback);

            await stepContext.Context.SendActivityAsync("Σ' ευχαριστώ πολύ για το σχόλιό σου! 😊");
            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion
    }
}
