using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class PreparationDialog : ComponentDialog
    {
        private readonly AspNetUserRepository userRepository;
        private readonly CourseRepository courseRepository;

        public PreparationDialog(PhoenixContext phoenixContext,
            AffiliatedUserPreparationComponent affiliatedUserPreparationComponent,
            CoursePreparationComponent coursePreparationComponent,
            GroupPreparationComponent groupPreparationComponent,
            DatePreparationComponent datePreparationComponent,
            LecturePreparationComponent lecturePreparationComponent)
            : base(nameof(PreparationDialog))
        {
            this.userRepository = new AspNetUserRepository(phoenixContext);
            this.courseRepository = new CourseRepository(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt), null, "fr-fr")); // Recognizes only: English, Spanish, French, Portuguese, Chinese, German, Italian, and Turkish

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Preparation.Top,
                new WaterfallStep[]
                {
                    RedirectStepAsync,
                    LoopStepAsync
                }));

            AddDialog(affiliatedUserPreparationComponent);
            AddDialog(coursePreparationComponent);
            AddDialog(groupPreparationComponent);
            AddDialog(datePreparationComponent);
            AddDialog(lecturePreparationComponent);

            InitialDialogId = WaterfallNames.Actions.Preparation.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> RedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var preparationOptions = stepContext.Options as PreparationOptions;
            var nextPreparation = preparationOptions.GetNextPreparation();

            PreparationComponentOptions preparationComponentOptions;

            //TODO: Customize Preparation Components by sending a more specific question/message to the user
            //depending on the reason they're asked to select for

            switch (nextPreparation)
            {
                case BotActionPreparation.AffiliatedUserSelection:
                    if (preparationOptions.AffiliatedUserId.HasValue)
                        return await stepContext.NextAsync(preparationOptions.AffiliatedUserId.Value, cancellationToken);

                    preparationComponentOptions = new PreparationComponentOptions(preparationOptions.UserId, true, preparationOptions);
                    break;
                case BotActionPreparation.CourseSelection:
                    if (preparationOptions.CourseId.HasValue)
                        return await stepContext.NextAsync(preparationOptions.CourseId.Value, cancellationToken);

                    int userIdToPrepareFor = preparationOptions.UserRole == Role.Parent
                        ? preparationOptions.AffiliatedUserId.Value
                        : preparationOptions.UserId;
                    preparationComponentOptions = new PreparationComponentOptions(userIdToPrepareFor, true, preparationOptions);
                    break;
                case BotActionPreparation.GroupSelection:
                    if (preparationOptions.CourseId.HasValue)
                        return await stepContext.NextAsync(preparationOptions.CourseId.Value, cancellationToken);

                    preparationComponentOptions = new PreparationComponentOptions(preparationOptions.UserId, true, preparationOptions);
                    break;
                case BotActionPreparation.DateSelection:
                case BotActionPreparation.DateExamSelection:
                    if (preparationOptions.DateToPrepareFor.HasValue)
                        return await stepContext.NextAsync(preparationOptions.DateToPrepareFor.Value, cancellationToken);

                    //if (preparationOptions.UserRole.IsStaff()) --> Teachers never get to select a specific course
                    if (!preparationOptions.CourseId.HasValue)
                        preparationComponentOptions = new PreparationComponentOptions(preparationOptions.UserId, true, preparationOptions);
                    else
                        preparationComponentOptions = new PreparationComponentOptions(preparationOptions.CourseId.Value, false, preparationOptions);

                    preparationComponentOptions.ExamsOnly = nextPreparation == BotActionPreparation.DateExamSelection;
                    break;
                case BotActionPreparation.LectureSelection:
                case BotActionPreparation.LectureExamSelection:
                    if (preparationOptions.LectureId.HasValue)
                        return await stepContext.NextAsync(preparationOptions.LectureId.Value, cancellationToken);

                    if (!preparationOptions.DateToPrepareFor.HasValue)
                        preparationOptions.DateToPrepareFor = DateTimeOffset.UtcNow.Date;

                    //if (preparationOptions.UserRole.IsStaff()) --> Teachers never get to select a specific course
                    if (!preparationOptions.CourseId.HasValue)
                        preparationComponentOptions = new PreparationComponentOptions(preparationOptions.UserId, true,
                            preparationOptions.DateToPrepareFor.Value, preparationOptions);
                    else
                        preparationComponentOptions = new PreparationComponentOptions(preparationOptions.CourseId.Value, false,
                            preparationOptions.DateToPrepareFor.Value, preparationOptions);

                    preparationComponentOptions.ExamsOnly = nextPreparation == BotActionPreparation.LectureExamSelection;
                    break;
                
                case BotActionPreparation.NoPreparation:
                default:
                    return await stepContext.EndDialogAsync(preparationOptions.GetActionOptions(), cancellationToken);
            }

            if (preparationComponentOptions == null)
            {
                await stepContext.Context.SendActivityAsync("Δυστυχώς υπήρξε κάποιο πρόβλημα. Ας ξεκινήσουμε από την αρχή.");
                return await stepContext.CancelAllDialogsAsync();
            }

            string nextPreparationDialogName = nextPreparation.ToString() + "_" + nameof(PreparationComponent);
            return await stepContext.BeginDialogAsync(nextPreparationDialogName, preparationComponentOptions, cancellationToken);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var preparationOptions = stepContext.Options as PreparationOptions;
            var curPreparation = preparationOptions.GetCurrentPreparation();

            switch (curPreparation)
            {
                case BotActionPreparation.AffiliatedUserSelection:
                    preparationOptions.AffiliatedUserId = (int)stepContext.Result;
                    break;
                case BotActionPreparation.CourseSelection:
                case BotActionPreparation.GroupSelection:
                    preparationOptions.CourseId = (int)stepContext.Result;
                    break;
                case BotActionPreparation.DateSelection:
                    preparationOptions.DateToPrepareFor = (DateTimeOffset)stepContext.Result;
                    break;
                case BotActionPreparation.LectureSelection:
                    preparationOptions.LectureId = (int)stepContext.Result;
                    break;
            }

            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, preparationOptions, cancellationToken);
        }

        #endregion
    }
}
