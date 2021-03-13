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

        public PreparationDialog(PhoenixContext phoenixContext) 
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

            AddDialog(new AffiliatedUserPreparationComponent());
            AddDialog(new CoursePreparationComponent());
            AddDialog(new GroupPreparationComponent());
            AddDialog(new DatePreparationComponent());
            AddDialog(new LecturePreparationComponent());

            InitialDialogId = WaterfallNames.Actions.Preparation.Top;
        }

        private async Task<PreparationComponentOptions> PrepareForUserAsync(PreparationOptions preparationOptions, int userIdToPrepareFor)
        {
            AspNetUsers userToPrepareFor;
            try
            {
                userToPrepareFor = await userRepository.Find(userIdToPrepareFor);
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            return new PreparationComponentOptions(userToPrepareFor, preparationOptions.GetUserOptions());
        }

        private async Task<PreparationComponentOptions> PrepareForCourseAsync(PreparationOptions preparationOptions, int courseIdToPrepareFor)
        {
            Course courseToPrepareFor;
            try
            {
                courseToPrepareFor = await courseRepository.Find(courseIdToPrepareFor);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            
            return new PreparationComponentOptions(courseToPrepareFor, preparationOptions.GetUserOptions());
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

                    preparationComponentOptions = await PrepareForUserAsync(preparationOptions, preparationOptions.UserId);
                    break;
                case BotActionPreparation.CourseSelection:
                    if (preparationOptions.CourseId != null)
                        return await stepContext.NextAsync(preparationOptions.CourseId, cancellationToken);

                    int userIdToPrepareFor = preparationOptions.UserRole == Role.Parent
                        ? (preparationOptions.AffiliatedUserId ?? 0) 
                        : preparationOptions.UserId;
                    preparationComponentOptions = await PrepareForUserAsync(preparationOptions, userIdToPrepareFor);
                    break;
                case BotActionPreparation.GroupSelection:
                    if (preparationOptions.CourseId != null)
                        return await stepContext.NextAsync(preparationOptions.CourseId, cancellationToken);

                    preparationComponentOptions = await PrepareForUserAsync(preparationOptions, preparationOptions.UserId);
                    break;
                case BotActionPreparation.DateSelection:
                    if (preparationOptions.DateToPrepareFor != null)
                        return await stepContext.NextAsync(preparationOptions.DateToPrepareFor, cancellationToken);

                    if (preparationOptions.UserRole.IsStaff())
                        preparationComponentOptions = await PrepareForUserAsync(preparationOptions, preparationOptions.UserId);
                    else
                        preparationComponentOptions = await PrepareForCourseAsync(preparationOptions, preparationOptions.CourseId ?? 0);

                    preparationComponentOptions.SelectTheClosestFutureDate = preparationOptions.SelectTheClosestFutureDate;
                    break;
                case BotActionPreparation.LectureSelection:
                    if (preparationOptions.LectureId != null)
                        return await stepContext.NextAsync(preparationOptions.LectureId, cancellationToken);

                    preparationComponentOptions = await PrepareForCourseAsync(preparationOptions, preparationOptions.CourseId ?? 0);
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

            string nextPreparationDialogName = WaterfallNames.Actions.Preparation.PreparationWaterfallName(nextPreparation);
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

            //TODO: Check ActiveDialog's value
            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, null, cancellationToken);
        }

        #endregion
    }
}
