using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Errors;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class CoursePreparation : PreparationComponent
    {
        protected override string AskPrompt => "Με ποιο μάθημα θα ήθελες να συνεχίσουμε;";
        protected override string AskRePrompt => "Παρακαλώ επίλεξε ή πληκτρολόγησε ένα από τα παρακάτω μαθήματα:";

        public CoursePreparation(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(BotActionPreparation.CourseSelection,
                  userState, convState, userManager, phoenixContext)
        {
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (PreparationComponentOptions)stepCtx.Options;

            var userToPrepareFor = await FindUserToPrepareForAsync(options);

            options.Selectables = GetSelectables(userToPrepareFor.Courses);

            if (!options.Selectables.Any())
            {
                var error = IsSelfPreparation(options)
                    ? BotError.UserNotEnrolledToCourses
                    : BotError.AffiliatedUserNotEnrolledToCourses;

                throw new BotException(error);
            }
           
            return await stepCtx.NextAsync(null, canTkn);
        }
    }
}
