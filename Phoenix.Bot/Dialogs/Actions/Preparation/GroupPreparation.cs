using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Errors;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class GroupPreparation : PreparationComponent
    {
        protected override string AskPrompt => "Με ποιο τμήμα θα ήθελες να συνεχίσουμε;";
        protected override string AskRePrompt => "Παρακαλώ επίλεξε ή πληκτρολόγησε ένα από τα παρακάτω τμήματα:";

        public GroupPreparation(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(BotActionPreparation.GroupSelection,
                  userState, convState, userManager, phoenixContext)
        {
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (PreparationComponentOptions)stepCtx.Options;

            var userToPrepareFor = await FindUserToPrepareForAsync(options);

            options.Selectables = GetSelectables(userToPrepareFor.Courses, showByGroup: true);

            if (!options.Selectables.Any())
                throw new BotException(BotError.UserNotEnrolledToCourses);

            return await stepCtx.NextAsync(null, canTkn);
        }
    }
}
