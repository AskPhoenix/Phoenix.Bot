using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Dialogs.Actions;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs
{
    public class HomeDialog : ComponentDialog
    {
        public HomeDialog(FeedbackDialog feedbackDialog, HelpDialog helpDialog,
            ExerciseDialog exerciseDialog, ExamDialog examDialog, ScheduleDialog scheduleDialog)
            : base(nameof(HomeDialog))
        {
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(feedbackDialog);
            AddDialog(helpDialog);

            AddDialog(exerciseDialog);
            AddDialog(examDialog);
            AddDialog(scheduleDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Home.Top,
                new WaterfallStep[]
                {
                    MenuStepAsync,
                    ActionStepAsync,
                    FeedbackStepAsync
                }));

            InitialDialogId = WaterfallNames.Home.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> MenuStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var homeOptions = stepContext.Options as HomeOptions;
            if (homeOptions.Action != BotAction.NoAction)
                return await stepContext.NextAsync(null, cancellationToken);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πώς θα μπορούσα να σε βοηθήσω;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω δυνατότητες:"),
                    Choices = ChoiceFactory.ToChoices(new string[] { "📚 Εργασίες", "📝 Διαγωνίσματα", "📅 Πρόγραμμα", "💪 Βοήθεια", "👍 Κάνε ένα σχόλιο" })
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> ActionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var homeOptions = stepContext.Options as HomeOptions;
            if (stepContext.Result is FoundChoice foundChoice)
                homeOptions.Action = (BotAction)(foundChoice.Index + 1);

            switch (homeOptions.Action)
            {
                case BotAction.Exercise:
                    return await stepContext.BeginDialogAsync(nameof(ExerciseDialog), null, cancellationToken);
                case BotAction.Exam:
                    return await stepContext.BeginDialogAsync(nameof(ExamDialog), null, cancellationToken);
                case BotAction.Schedule:
                    return await stepContext.BeginDialogAsync(nameof(ScheduleDialog), null, cancellationToken);
                case BotAction.Help:
                    return await stepContext.BeginDialogAsync(nameof(HelpDialog), new HelpOptions(), cancellationToken);
                case BotAction.Feedback:
                    var feedbackOptions = new FeedbackOptions()
                    {
                        BotAskedForFeedback = false,
                        UserId = homeOptions.UserId
                    };
                    return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, cancellationToken);
                default:
                    return await stepContext.EndDialogAsync(null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> FeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var homeOptions = stepContext.Options as HomeOptions;
            if (homeOptions.Action != BotAction.Feedback && homeOptions.Action != BotAction.Help && new Random().Next(3) == 0)
            {
                var feedbackOptions = new FeedbackOptions()
                {
                    BotAskedForFeedback = true,
                    UserId = homeOptions.UserId
                };
                return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, cancellationToken);
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        #endregion

    }
}
