﻿using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.State.Options.Actions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public abstract class PreparationComponent : ComponentDialog
    {
        protected PreparationComponent(BotActionPreparation preparation)
            : base(preparation.ToString() + "_" + nameof(PreparationComponent))
        {
            string wfName = WaterfallNames.Actions.Preparation.PreparationWaterfallName(preparation);

            AddDialog(new WaterfallDialog(wfName,
                new WaterfallStep[]
                {
                    InitializeStepAsync,
                    AskStepAsync,
                    SelectStepAsync
                }));

            InitialDialogId = wfName;
        }

        protected abstract Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken);
        protected abstract Task<DialogTurnResult> AskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken);
        
        protected virtual async Task<DialogTurnResult> SelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            var selectedIndex = (stepContext.Result as FoundChoice).Index;

            return await stepContext.EndDialogAsync(options.Selectables.ElementAt(selectedIndex).Key, cancellationToken);
        }
    }
}
