using Phoenix.Bot.Dialogs.Actions;
using Phoenix.Bot.Dialogs.Actions.Preparation;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;

namespace Phoenix.Bot.Dialogs
{
    public class HomeDialog : StateDialog
    {
        public HomeDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext,

            PreparationDialog preparationDialog,
            AccessDialog accessDialog,
            AssignmentsDialog assignmentsDialog,
            TeacherExtensionDialog assignmentsManagementDialog,
            BroadcastDialog broadcastDialog,
            GradesDialog gradesDialog,
            ScheduleDialog scheduleDialog,
            HelpDialog helpDialog,
            FeedbackDialog feedbackDialog)
            : base(userState, convState, userManager, phoenixContext, nameof(HomeDialog))
        {
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(preparationDialog);
            AddDialog(accessDialog);
            AddDialog(assignmentsDialog);
            AddDialog(assignmentsManagementDialog);
            AddDialog(broadcastDialog);
            AddDialog(gradesDialog);
            AddDialog(scheduleDialog);
            AddDialog(helpDialog);
            AddDialog(feedbackDialog);

            AddDialog(new WaterfallDialog(WaterfallNames.Home.Top,
                new WaterfallStep[]
                {
                    MenuStepAsync,
                    CheckActionStepAsync,
                    ActionStepAsync,
                    FeedbackStepAsync
                }));

            InitialDialogId = WaterfallNames.Home.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> MenuStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (HomeOptions)stepCtx.Options;

            if (options.Action != BotAction.NoAction)
                return await stepCtx.NextAsync(null, canTkn);

            var menuActions = BotActionExtensions.FindMenuActions(UData.SelectedRole!.Value);
            var choices = ChoiceFactory.ToChoices(
                menuActions.Select(a => a.ToFriendlyString(addEmoji: true)).ToList());

            return await stepCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Πώς θα μπορούσα να σε βοηθήσω;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω δυνατότητες:"),
                    Choices = choices,
                    Style = ListStyle.SuggestedAction
                },
                canTkn);
        }

        private async Task<DialogTurnResult> CheckActionStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (HomeOptions)stepCtx.Options;
            var userRole = UData.SelectedRole!.Value;
            var actions = BotActionExtensions.FindMenuActions(userRole);

            if (stepCtx.Result is FoundChoice foundChoice)
                options.Action = actions.ElementAt(foundChoice.Index);

            bool isValidAction = actions.Contains(options.Action) || options.Action.IsNonMenuAction();
            if (!isValidAction)
            {
                await stepCtx.Context.SendActivityAsync(BotError.ActionForbidden.GetMessage(),
                    cancellationToken: canTkn);

                return await stepCtx.EndDialogAsync(null, canTkn);
            }

            var preparations = options.Action.FindPreparations(userRole);
            var preparationOptions = new PreparationOptions(preparations);

            return await stepCtx.BeginDialogAsync(nameof(PreparationDialog), preparationOptions, canTkn);
        }

        private async Task<DialogTurnResult> ActionStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            if (stepCtx.Result is null)
                return await stepCtx.EndDialogAsync(null, canTkn);

            var options = (HomeOptions)stepCtx.Options;
            var actionOptions = (ActionOptions)stepCtx.Result;

            switch (options.Action)
            {
                case BotAction.SearchExercises:
                case BotAction.Assignments:
                    AssignmentsOptions assignmentsOptions = new(actionOptions)
                    {
                        Search = options.Action == BotAction.SearchExercises
                    };

                    return await stepCtx.BeginDialogAsync(nameof(AssignmentsDialog), assignmentsOptions, canTkn);
                
                case BotAction.Supplementary:
                    goto default;

                case BotAction.ScheduleWeek:
                case BotAction.ScheduleDay:
                    ScheduleOptions scheduleOptions = new(actionOptions)
                    {
                        Daily = options.Action == BotAction.ScheduleDay
                    };
                    return await stepCtx.BeginDialogAsync(nameof(ScheduleDialog), scheduleOptions, canTkn);
                
                case BotAction.Grades:
                    return await stepCtx.BeginDialogAsync(nameof(GradesDialog), actionOptions, canTkn);

                case BotAction.SearchExams:
                    return await stepCtx.BeginDialogAsync(nameof(GradesDialog), actionOptions, canTkn);

                case BotAction.Access:
                    return await stepCtx.BeginDialogAsync(nameof(AccessDialog), actionOptions, canTkn);

                case BotAction.Exercises:
                case BotAction.Exams:
                    TeacherExtensionOptions teacherExtensionOptions = new(actionOptions)
                    {
                        ExtensionAction = options.Action
                    };

                    return await stepCtx.BeginDialogAsync(nameof(TeacherExtensionDialog), teacherExtensionOptions, canTkn);
                
                case BotAction.Broadcast:
                    var broadcastOptions = new BroadcastOptions(actionOptions);

                    return await stepCtx.BeginDialogAsync(nameof(BroadcastDialog), broadcastOptions, canTkn);

                case BotAction.Help:
                    return await stepCtx.BeginDialogAsync(nameof(HelpDialog), new HelpOptions(), canTkn);
                
                case BotAction.Feedback:
                    var feedbackOptions = new FeedbackOptions()
                    {
                        BotAskedForFeedback = false
                    };
                    return await stepCtx.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, canTkn);

                case BotAction.NoAction:
                
                default:
                    await stepCtx.Context.SendActivityAsync(BotError.ActionNotAvailable.GetMessage(),
                        cancellationToken: canTkn);
                    return await stepCtx.EndDialogAsync(null, canTkn);
            }
        }

        private async Task<DialogTurnResult> FeedbackStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (HomeOptions)stepCtx.Options;

            if (stepCtx.Result is BotAction nextAction)
            {
                options.Action = nextAction;
                return await stepCtx.ReplaceDialogAsync(nameof(HomeDialog), options, canTkn);
            }

            if (options.Action != BotAction.Feedback && options.Action != BotAction.Help
                && new Random().Next(5) == 0)
            {
                var feedbackOptions = new FeedbackOptions() { BotAskedForFeedback = true };
                return await stepCtx.BeginDialogAsync(nameof(FeedbackDialog), feedbackOptions, canTkn);
            }

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        #endregion
    }
}
