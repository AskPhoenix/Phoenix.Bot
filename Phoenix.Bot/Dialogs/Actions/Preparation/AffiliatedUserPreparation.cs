using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Errors;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class AffiliatedUserPreparation : PreparationComponent
    {
        protected override string AskPrompt => "Σχετικά με ποιον/ποια θα ήθελες να ενημερωθείς;";
        protected override string AskRePrompt => "Παρακαλώ επίλεξε ή πληκτρολόγησε ένα από τα παρακάτω ονόματα:";

        public AffiliatedUserPreparation(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(BotActionPreparation.AffiliatedUserSelection,
                  userState, convState, userManager, phoenixContext)
        {
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (PreparationComponentOptions)stepCtx.Options;

            var userToPrepareFor = await FindUserToPrepareForAsync(options);

            options.Selectables = GetSelectables(userToPrepareFor.Children);

            if (!options.Selectables.Any())
                throw new BotException(BotError.ParentHasNoAffiliations);

            return await stepCtx.NextAsync(null, canTkn);
        }
    }
}
