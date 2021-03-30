﻿using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class AffiliatedUserPreparationComponent : PreparationComponent
    {
        private readonly AspNetUserRepository userRepository;

        public AffiliatedUserPreparationComponent(PhoenixContext phoenixContext) 
            : base(BotActionPreparation.AffiliatedUserSelection) 
        {
            this.userRepository = new AspNetUserRepository(phoenixContext);
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            var children = userRepository.FindChildren(options.IdToPrepareFor);

            options.Selectables = PreparationComponentHelper.GetSelectables(children);

            if (options.Selectables is null || !options.Selectables.Any())
            {
                await stepContext.Context.SendActivityAsync("Δεν υπάρχουν μαθητές οι οποίοι να είναι συσχετισμένοι με τον λογαριασμό σου.");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }
            else if (options.Selectables.Count == 1) 
                return await stepContext.EndDialogAsync(options.Selectables.Single().Key);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        protected override async Task<DialogTurnResult> AskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Σχετικά με ποιον/ποια θα ήθελες να ενημερωθείς;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε ένα από τα παρακάτω ονόματα:"),
                    Choices = ChoiceFactory.ToChoices(options.Selectables.Values.ToList())
                },
                cancellationToken);
        }
    }
}
