using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Miscellaneous;
using Phoenix.Bot.Utilities.State.Options.Actions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class DatePreparationComponent : PreparationComponent
    {
        public DatePreparationComponent()
            : base(BotActionPreparation.DateSelection) { }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            bool singleCourse = options.CourseToPrepareFor != null;

            options.Selectables = PreparationComponentHelper.GetDateSelectables(options);

            if (options.Selectables == null || options.Selectables.Count == 0)
            {
                string msg = "Δεν υπάρχουν ακόμα διαλέξεις ";
                if (singleCourse)
                    msg += "για αυτό το μάθημα";
                else
                    msg += "για κάποιο μάθημα";

                await stepContext.Context.SendActivityAsync(msg);
                return await stepContext.CancelAllDialogsAsync(cancellationToken);
            }
            else if (options.Selectables.Count == 1)
                return await stepContext.EndDialogAsync(CalendarExtensions.ParseDate(options.Selectables.Single().Value), cancellationToken);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        protected override async Task<DialogTurnResult> AskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;

            await stepContext.Context.SendActivityAsync("Παρακάτω θα βρεις μερικές από τις πιο κοντινές ημερομηνίες που είχες ή θα έχεις μάθημα.");

            var prompt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(options.Selectables.Values.ToList()),
                text: "Επίλεξε μία από αυτές ή πληκτρολόγησε κάποια άλλη παρακάτω στη μορφή ηη/μμ (π.χ. 24/4):");
            var repropmt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(options.Selectables.Values.ToList()),
                text: "Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ημέρα/μήνας (π.χ. 24/4)):");

            return await stepContext.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = (Activity)prompt,
                    RetryPrompt = (Activity)repropmt
                });
        }

        protected override async Task<DialogTurnResult> SelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            var selectedIndex = (stepContext.Result as FoundChoice).Index;
            var selectedDate = options.Selectables.ElementAt(selectedIndex).Value;

            return await stepContext.EndDialogAsync(CalendarExtensions.ParseDate(selectedDate), cancellationToken);
        }
    }
}
