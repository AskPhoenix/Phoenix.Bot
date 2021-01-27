using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Dialogs.Common;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Student
{
    public class StudentDialog : ComponentDialog
    {
        public StudentDialog(FeedbackDialog feedbackDialog, HelpDialog helpDialog,
            ExerciseDialog exerciseDialog, ExamDialog examDialog, ScheduleDialog scheduleDialog)
            : base(nameof(StudentDialog))
        {
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(feedbackDialog);
            AddDialog(helpDialog);

            AddDialog(exerciseDialog);
            AddDialog(examDialog);
            AddDialog(scheduleDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Student.Home,
                new WaterfallStep[]
                {
                    MenuStepAsync,
                    ActionStepAsync,
                    FeedbackStepAsync,
                    LoopStepAsync
                }));

            InitialDialogId = WaterfallNames.Student.Home;
        }

        #region Home Waterfall Dialog

        private async Task<DialogTurnResult> MenuStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var studentOptions = stepContext.Options as StudentOptions;
            if (studentOptions.Action != StudentAction.NoAction)
                return await stepContext.NextAsync(null, cancellationToken);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πώς θα μπορούσα να σε βοηθήσω;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω απαντήσεις:"),
                    Choices = ChoiceFactory.ToChoices(new string[] { "📚 Εργασίες", "📝 Διαγωνίσματα", "📅 Πρόγραμμα", "💪 Βοήθεια", "👍 Κάνε ένα σχόλιο" })
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> ActionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var studentOptions = stepContext.Options as StudentOptions;
            if (stepContext.Result is FoundChoice foundChoice)
                studentOptions.Action = (StudentAction)(foundChoice.Index + 1);

            switch (studentOptions.Action)
            {
                case StudentAction.Exercises:
                    return await stepContext.BeginDialogAsync(nameof(ExerciseDialog), null, cancellationToken);
                case StudentAction.Exams:
                    return await stepContext.BeginDialogAsync(nameof(ExamDialog), null, cancellationToken);
                case StudentAction.Schedule:
                    return await stepContext.BeginDialogAsync(nameof(ScheduleDialog), null, cancellationToken);
                case StudentAction.Help:
                    return await stepContext.BeginDialogAsync(nameof(HelpDialog), new HelpOptions(), cancellationToken);
                case StudentAction.Feedback:
                    var feedbackOptions = new FeedbackOptions()
                    {
                        BotAskedForFeedback = false,
                        UserId = studentOptions.StudentId
                    };
                    return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, cancellationToken);
                default:
                    return await stepContext.EndDialogAsync(null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> FeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var studentOptions = stepContext.Options as StudentOptions;
            if (studentOptions.Action != StudentAction.Feedback && studentOptions.Action != StudentAction.Help && new Random().Next(3) == 0)
            {
                var feedbackOptions = new FeedbackOptions() 
                {
                    BotAskedForFeedback = true,
                    UserId = studentOptions.StudentId
                };
                return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, cancellationToken);
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var studentOptions = stepContext.Options as StudentOptions;
            studentOptions.Action = StudentAction.NoAction;

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, studentOptions, cancellationToken);
        }

        #endregion
    }
}
