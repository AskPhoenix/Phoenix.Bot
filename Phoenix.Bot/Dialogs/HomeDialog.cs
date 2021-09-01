using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Dialogs.Actions;
using Phoenix.Bot.Dialogs.Actions.Preparation;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options;
using Phoenix.Bot.Utilities.State.Options.Actions;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs
{
    public class HomeDialog : ComponentDialog
    {
        public HomeDialog(
            PreparationDialog preparationDialog,
            AssignmentsDialog assignmentsDialog,
            AccessDialog accessDialog,
            GradesDialog gradesDialog, ScheduleDialog scheduleDialog,
            AssignmentsManagementDialog assignmentsManagementDialog,
            BroadcastDialog broadcastDialog,
            HelpDialog helpDialog, FeedbackDialog feedbackDialog)
            : base(nameof(HomeDialog))
        {
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(preparationDialog);

            AddDialog(assignmentsDialog);

            AddDialog(accessDialog);

            AddDialog(scheduleDialog);
            AddDialog(gradesDialog);

            AddDialog(assignmentsManagementDialog);
            AddDialog(broadcastDialog);

            AddDialog(helpDialog);
            AddDialog(feedbackDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Home.Top,
                new WaterfallStep[]
                {
                    MenuStepAsync,
                    CheckActionValidity,
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
                    Choices = BotActionHelper.GetActionChoices(homeOptions.UserRole, removePendingActions: true),
                    Style = ListStyle.SuggestedAction
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> CheckActionValidity(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var homeOptions = stepContext.Options as HomeOptions;
            if (stepContext.Result is FoundChoice foundChoice)
                homeOptions.Action = BotActionHelper.GetMenuActions(homeOptions.UserRole, removePendingActions: true).ElementAt(foundChoice.Index);

            bool isValidAction = BotActionHelper.GetMenuActions(homeOptions.UserRole).Contains(homeOptions.Action) 
                || homeOptions.Action.IsNonMenuAction();
            if (!isValidAction)
            {
                await stepContext.Context.SendActivityAsync("Δεν έχεις πρόσβαση στη δυνατότητα που προσπαθείς να εισέλθεις. Παρακαλώ επίλεξε μία έγκυρη.");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            var preparations = BotActionPreparationHelper.GetPreparations(homeOptions.Action, homeOptions.UserRole);
            var preparationOptions = new PreparationOptions(preparations, homeOptions);

            return await stepContext.BeginDialogAsync(nameof(PreparationDialog), preparationOptions, cancellationToken);
        }

        private async Task<DialogTurnResult> ActionStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is null)
                return await stepContext.EndDialogAsync(null, cancellationToken);

            var homeOptions = stepContext.Options as HomeOptions;
            var actionOptions = stepContext.Result as ActionOptions;

            switch (homeOptions.Action)
            {
                case BotAction.SearchExercises:
                case BotAction.Assignments:
                    AssignmentsOptions assignmentsOptions = new(actionOptions, homeOptions.Action);
                    return await stepContext.BeginDialogAsync(nameof(AssignmentsDialog), assignmentsOptions, cancellationToken);
                case BotAction.Supplementary:
                    goto default;

                case BotAction.ScheduleDaily:
                case BotAction.ScheduleWeekly:
                    ScheduleOptions scheduleOptions = new(actionOptions, homeOptions.Action);
                    return await stepContext.BeginDialogAsync(nameof(ScheduleDialog), scheduleOptions, cancellationToken);
                case BotAction.Grades:
                    return await stepContext.BeginDialogAsync(nameof(GradesDialog), actionOptions, cancellationToken);
                case BotAction.SearchExams:
                    return await stepContext.BeginDialogAsync(nameof(GradesDialog), actionOptions, cancellationToken);

                case BotAction.Access:
                    return await stepContext.BeginDialogAsync(nameof(AccessDialog), actionOptions, cancellationToken);

                case BotAction.Exercises:
                case BotAction.Exams:
                    AssignmentsManagementOptions assignmentsManagementOptions = new(actionOptions, homeOptions.Action);
                    return await stepContext.BeginDialogAsync(nameof(AssignmentsManagementDialog), assignmentsManagementOptions, cancellationToken);
                case BotAction.Broadcast:
                    return await stepContext.BeginDialogAsync(nameof(BroadcastDialog), new BroadcastOptions(actionOptions), cancellationToken);

                case BotAction.Help:
                    return await stepContext.BeginDialogAsync(nameof(HelpDialog), new HelpOptions(), cancellationToken);
                case BotAction.Feedback:
                    var feedbackOptions = new FeedbackOptions()
                    {
                        BotAskedForFeedback = false,
                        UserId = homeOptions.UserId
                    };
                    return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, cancellationToken);

                case BotAction.NoAction:
                default:
                    await stepContext.Context.SendActivityAsync("Η ενέργεια που ζητήσατε δεν είναι διαθέσιμη προς το παρόν.");
                    return await stepContext.EndDialogAsync(null, cancellationToken);
            }
        }

        private async Task<DialogTurnResult> FeedbackStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var homeOptions = stepContext.Options as HomeOptions;
            var resultAction = stepContext.Result as BotAction?;

            if (stepContext.Result is BotAction nextAction)
            {
                homeOptions.Action = nextAction;
                return await stepContext.ReplaceDialogAsync(nameof(HomeDialog), homeOptions, cancellationToken);
            }

            if (homeOptions.Action != BotAction.Feedback && homeOptions.Action != BotAction.Help && new Random().Next(3) == 0)
            {
                var feedbackOptions = new FeedbackOptions() { BotAskedForFeedback = true, UserId = homeOptions.UserId };
                return await stepContext.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, cancellationToken);
            }

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        #endregion
    }
}
