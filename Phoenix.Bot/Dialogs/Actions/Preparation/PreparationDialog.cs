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

            switch (nextPreparation)
            {
                case BotActionPreparation.AffiliatedUserSelection:
                    if (preparationOptions.AffiliatedUserId != null)
                        return await stepContext.NextAsync(preparationOptions.AffiliatedUserId, cancellationToken);

                    preparationComponentOptions = new PreparationComponentOptions(preparationOptions.UserId, true, preparationOptions.GetUserOptions());
                    break;
                case BotActionPreparation.CourseSelection:
                    if (preparationOptions.CourseId != null)
                        return await stepContext.NextAsync(preparationOptions.CourseId, cancellationToken);

                    int userIdToPrepareFor = preparationOptions.UserRole == Role.Parent
                        ? (preparationOptions.AffiliatedUserId ?? 0) 
                        : preparationOptions.UserId;
                    preparationComponentOptions = new PreparationComponentOptions(userIdToPrepareFor, true, preparationOptions.GetUserOptions());
                    break;
                case BotActionPreparation.GroupSelection:
                    if (preparationOptions.CourseId != null)
                        return await stepContext.NextAsync(preparationOptions.CourseId, cancellationToken);

                    preparationComponentOptions = new PreparationComponentOptions(preparationOptions.UserId, true, preparationOptions.GetUserOptions());
                    break;
                case BotActionPreparation.DateSelection:
                    if (preparationOptions.DateToPrepareFor != null)
                        return await stepContext.NextAsync(preparationOptions.DateToPrepareFor, cancellationToken);

                    if (preparationOptions.UserRole.IsStaff())
                        preparationComponentOptions = new PreparationComponentOptions(preparationOptions.UserId, true, preparationOptions.GetUserOptions());
                    else
                        preparationComponentOptions = new PreparationComponentOptions(preparationOptions.CourseId ?? 0, false, preparationOptions.GetUserOptions());

                    preparationComponentOptions.SelectTheClosestFutureDate = preparationOptions.SelectTheClosestFutureDate;
                    break;
                case BotActionPreparation.LectureSelection:
                    if (preparationOptions.LectureId != null)
                        return await stepContext.NextAsync(preparationOptions.LectureId, cancellationToken);

                    preparationComponentOptions = new PreparationComponentOptions(preparationOptions.CourseId ?? 0, false, 
                        preparationOptions.DateToPrepareFor ?? DateTimeOffset.UtcNow.Date, preparationOptions.GetUserOptions());
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
