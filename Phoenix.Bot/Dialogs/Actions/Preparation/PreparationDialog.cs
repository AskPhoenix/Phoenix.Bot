using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class PreparationDialog : StateDialog
    {
        public PreparationDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext,

            AffiliatedUserPreparation affiliatedUserPreparation,
            CoursePreparation coursePreparation,
            DatePreparation datePreparation,
            GroupPreparation groupPreparation,
            LecturePreparation lecturePreparation)
            : base(userState,convState, userManager, phoenixContext, nameof(PreparationDialog))
        {
            // Recognizes only: English, Spanish, French, Portuguese, Chinese, German, Italian, and Turkish
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt), PromptValidators.CustomDateTimePromptValidator, "fr-FR"));
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            
            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Preparation.Top,
                new WaterfallStep[]
                {
                    RedirectStepAsync,
                    LoopStepAsync
                }));

            AddDialog(affiliatedUserPreparation);
            AddDialog(coursePreparation);
            AddDialog(datePreparation);
            AddDialog(groupPreparation);
            AddDialog(lecturePreparation);

            InitialDialogId = WaterfallNames.Actions.Preparation.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> RedirectStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (PreparationOptions)stepCtx.Options;

            bool done = !options.Preparations.TryPeek(out BotActionPreparation nextPrep);
            if (done || nextPrep == BotActionPreparation.NoPreparation)
                return await stepCtx.EndDialogAsync(options.GetActionOptions(), canTkn);

            // If all preparation component options are null, then the selection concerns the current user
            var prepComponentOptions = new PreparationComponentOptions() 
            {
                ExamsOnly = nextPrep == BotActionPreparation.DateExamSelection ||
                            nextPrep == BotActionPreparation.LectureExamSelection
            };

            switch (nextPrep)
            {
                case BotActionPreparation.AffiliatedUserSelection:
                case BotActionPreparation.GroupSelection:
                default:
                    break;

                case BotActionPreparation.CourseSelection:
                    if (UData.SelectedRole == RoleRank.Parent && options.AffiliatedUserId is null)
                    {
                        if (options.AffiliatedUserId is null)
                        {
                            options.Preparations.Push(BotActionPreparation.AffiliatedUserSelection);
                            goto case BotActionPreparation.AffiliatedUserSelection;
                        }

                        prepComponentOptions = new(userIdToPrepareFor: options.AffiliatedUserId);
                    }
                    break;

                case BotActionPreparation.DateSelection:
                case BotActionPreparation.DateExamSelection:
                    // Teachers never get to select a specific course
                    if (options.CourseId.HasValue)
                        prepComponentOptions = new(courseIdToPrepareFor: options.CourseId);
                    break;

                case BotActionPreparation.LectureSelection:
                case BotActionPreparation.LectureExamSelection:
                    // Teachers never get to select a specific course
                    if (options.CourseId.HasValue)
                        prepComponentOptions = new(courseIdToPrepareFor: options.CourseId,
                            dateToPrepareFor: options.DateToPrepareFor);
                    else
                        prepComponentOptions = new(dateToPrepareFor: options.DateToPrepareFor);
                    break;
            }

            return await stepCtx.BeginDialogAsync(
                nextPrep.PreparationComponentName(), prepComponentOptions, canTkn);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            if (stepCtx.Result is null)
                return await stepCtx.EndDialogAsync(null, canTkn);

            var options = (PreparationOptions)stepCtx.Options;
            var curPrep = options.Preparations.Pop();

            switch (curPrep)
            {
                case BotActionPreparation.AffiliatedUserSelection:
                    options.AffiliatedUserId = (int)stepCtx.Result;
                    break;

                case BotActionPreparation.CourseSelection:
                case BotActionPreparation.GroupSelection:
                    options.CourseId = (int)stepCtx.Result;
                    break;

                case BotActionPreparation.DateSelection:
                    options.DateToPrepareFor = (DateTimeOffset)stepCtx.Result;
                    break;

                case BotActionPreparation.LectureSelection:
                    options.LectureId = (int)stepCtx.Result;
                    break;
            }

            return await stepCtx.ReplaceDialogAsync(stepCtx.ActiveDialog.Id, options, canTkn);
        }

        #endregion
    }
}
