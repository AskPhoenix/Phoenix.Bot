using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Miscellaneous;
using Phoenix.Bot.Utilities.State.Options.Actions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class LecturePreparationComponent : PreparationComponent
    {
        public LecturePreparationComponent ()
            : base(BotActionPreparation.LectureSelection) { }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            bool singleCourse = options.CourseToPrepareFor != null;
            var dateToPrepareFor = options.DateToPrepareFor.Value;

            options.Selectables = PreparationComponentHelper.GetLectureSelectables(options, dateToPrepareFor);
            
            if (options.Selectables == null || options.Selectables.Count == 0)
            {
                var kati = PreparationComponentHelper.GetDateSelectables(options, daysNum: 1).Single().Value;
                var closestDate = CalendarExtensions.ParseDate(kati);

                options.Selectables = PreparationComponentHelper.GetLectureSelectables(options, closestDate);

                string msg = $"Δεν υπάρχουν διαλέξεις στις {dateToPrepareFor:d/M}";
                if (singleCourse)
                    msg += " για αυτό το μάθημα.";
                else
                    msg += ".";
                await stepContext.Context.SendActivityAsync(msg);
                await stepContext.Context.SendActivityAsync($"Βρήκα όμως για την πιο κοντινή στις {closestDate:d/M}:");
            }
            
            if (options.Selectables.Count == 1)
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
                    Prompt = MessageFactory.Text("Με ποια διάλεξη (ώρα μαθήματος) θα ήθελες να συνεχίσουμε;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω (ώρες):"),
                    Choices = ChoiceFactory.ToChoices(options.Selectables.Values.ToList())
                },
                cancellationToken);
        }
    }
}
