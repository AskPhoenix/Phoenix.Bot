using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class CoursePreparationComponent : PreparationComponent
    {
        private readonly CourseRepository courseRepository;

        public CoursePreparationComponent (PhoenixContext phoenixContext)
            : base(BotActionPreparation.CourseSelection) 
        {
            this.courseRepository = new CourseRepository(phoenixContext);
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            var courses = courseRepository.FindForUser(options.IdToPrepareFor, options.UserRole.IsStaff());

            options.Selectables = PreparationComponentHelper.GetSelectables(courses);

            if (options.Selectables is null || !options.Selectables.Any())
            {
                if (options.IsPreparingForSomeoneElse)
                    await stepContext.Context.SendActivityAsync("Δεν έχουν προστεθεί μαθήματα για τον συγκεκριμένο μαθητή.");
                else
                    await stepContext.Context.SendActivityAsync("Δεν έχεις εγγραφεί ακόμη σε κάποιο μάθημα.");

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
                    Prompt = MessageFactory.Text("Με ποιο μάθημα θα ήθελες να συνεχίσουμε;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε ένα από τα παρακάτω μαθήματα:"),
                    Choices = ChoiceFactory.ToChoices(options.Selectables.Values.ToList())
                },
                cancellationToken);
        }
    }
}
