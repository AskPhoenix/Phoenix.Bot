using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class PreparationDialog : ComponentDialog
    {
        private readonly AspNetUserRepository userRepository;

        public PreparationDialog(PhoenixContext phoenixContext) 
            : base(nameof(PreparationDialog))
        {
            this.userRepository = new AspNetUserRepository(phoenixContext);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Preparation.Top,
                new WaterfallStep[]
                {
                    RedirectStepAsync,
                    LoopStepAsync
                }));

            AddDialog(new AffiliatedUserWaterfallDialog());
            AddDialog(new CourseWaterfallDialog());
            //AddDialog(new LectureWaterfallDialog());
            //AddDialog(new GroupWaterfallDialog());

            InitialDialogId = WaterfallNames.Actions.Preparation.Top;
        }

        #region Top Waterfall Dialog

        private async Task<DialogTurnResult> RedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var preparationOptions = stepContext.Options as PreparationOptions;
            var nextPreparation = preparationOptions.GetNextPreparation();

            AspNetUsers userToPrepareFor;
            PreparationComponentOptions preparationComponentOptions;

            switch (nextPreparation)
            {
                case BotActionPreparation.AffiliatedUserSelection:
                    userToPrepareFor = await userRepository.Find(preparationOptions.UserId);    // Prepare for parent
                    preparationComponentOptions = new PreparationComponentOptions(userToPrepareFor, preparationOptions.GetUserOptions());
                    break;
                case BotActionPreparation.CourseSelection:
                    if (preparationOptions.UserRole == Role.Parent)
                        userToPrepareFor = await userRepository.Find(preparationOptions.AffiliatedUserId ?? 0);
                    else
                        userToPrepareFor = await userRepository.Find(preparationOptions.UserId);

                    preparationComponentOptions = new PreparationComponentOptions(userToPrepareFor, preparationOptions.GetUserOptions());
                    break;
                //case BotActionPreparation.LectureSelection:
                //    break;
                //case BotActionPreparation.GroupSelection:
                //    break;
                
                case BotActionPreparation.NoPreparation:
                default:
                    return await stepContext.EndDialogAsync(preparationOptions.GetActionOptions(), cancellationToken);
            }

            string nextPreparationDialogName = WaterfallNames.Actions.Preparation.PreparationWaterfallName(nextPreparation);
            return await stepContext.BeginDialogAsync(nextPreparationDialogName, preparationComponentOptions, cancellationToken);
        }

        private async Task<DialogTurnResult> LoopStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var preparationOptions = stepContext.Options as PreparationOptions;
            var curPreparation = preparationOptions.GetCurrentPreparation();
            int selected = (int)stepContext.Result;

            switch (curPreparation)
            {
                case BotActionPreparation.AffiliatedUserSelection:
                    preparationOptions.AffiliatedUserId = selected;
                    break;
                case BotActionPreparation.CourseSelection:
                    preparationOptions.CourseId = selected;
                    break;
                //case BotActionPreparation.LectureSelection:
                //    break;
                //case BotActionPreparation.GroupSelection:
                //    break;
            }

            //TODO: Check ActiveDialog's value
            return await stepContext.ReplaceDialogAsync(stepContext.ActiveDialog.Id, null, cancellationToken);
        }

        #endregion
    }
}
