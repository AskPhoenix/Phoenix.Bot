using Microsoft.Bot.Builder;
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
    public class GroupPreparationComponent : PreparationComponent
    {
        private readonly CourseRepository courseRepository;

        public GroupPreparationComponent (PhoenixContext phoenixContext)
            : base(BotActionPreparation.GroupSelection) 
        {
            this.courseRepository = new CourseRepository(phoenixContext);
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            var courses = courseRepository.FindForTeacher(options.IdToPrepareFor);

            options.Selectables = PreparationComponentHelper.GetSelectables(courses, showByGroup: true);

            if (options.Selectables == null || options.Selectables.Count == 0)
            {
                await stepContext.Context.SendActivityAsync("Δεν έχεις αναλάβει ακόμη τη διδασκαλία κάποιου μαθήματος.");
                return await stepContext.CancelAllDialogsAsync(cancellationToken);
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
                    Prompt = MessageFactory.Text("Με ποιο τμήμα θα ήθελες να συνεχίσουμε;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε ένα από τα παρακάτω τμήματα:"),
                    Choices = ChoiceFactory.ToChoices(options.Selectables.Values.ToList())
                },
                cancellationToken);
        }
    }
}
