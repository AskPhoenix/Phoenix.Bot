using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Miscellaneous;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class DatePreparationComponent : PreparationComponent
    {
        private readonly CourseRepository courseRepository;
        private readonly LectureRepository lectureRepository;

        public DatePreparationComponent(PhoenixContext phoenixContext)
            : base(BotActionPreparation.DateSelection) 
        {
            this.courseRepository = new CourseRepository(phoenixContext);
            this.lectureRepository = new LectureRepository(phoenixContext);
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            bool singleCourse = !options.PrepareForUserOrCourse;
            IQueryable<DateTime> dates;

            if (singleCourse)
            {
                if (options.SelectTheClosestFutureDate)
                    dates = lectureRepository.FindClosestLectureDates(options.IdToPrepareFor, Tense.Future, dayRange: 1, scheduledOnly: true);
                else
                    dates = lectureRepository.FindClosestLectureDates(options.IdToPrepareFor, Tense.Anytime, scheduledOnly: true);
            }
            else
            {
                int[] courseIds = courseRepository.FindForTeacher(options.IdToPrepareFor).Select(c => c.Id).ToArray();
                if (options.SelectTheClosestFutureDate)
                    dates = lectureRepository.FindClosestLectureDates(courseIds, Tense.Future, dayRange: 1);
                else
                    dates = lectureRepository.FindClosestLectureDates(courseIds, Tense.Anytime);
            }

            options.Selectables = PreparationComponentHelper.GetSelectables(dates);

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
            bool singleCourse = !options.PrepareForUserOrCourse;

            string msg = "Παρακάτω θα βρεις μερικές από τις πιο κοντινές ημερομηνίες ";
            if (singleCourse)
                msg += "που είχες μάθημα.";
            else
                msg += "που είχες ή θα έχεις μάθημα";

            await stepContext.Context.SendActivityAsync(msg);
            
            var choices = ChoiceFactory.ToChoices(options.Selectables.Values.ToList());
            var today = DateTimeOffset.UtcNow.Date;

            //TODO: Check if choices are valid
            for (int i = 0; i < choices.Count; i++)
            {
                var date = CalendarExtensions.ParseDate(choices[i].Value).Date;
                if (date == today)
                {
                    choices[i].Synonyms.Add(choices[i].Value);
                    choices[i].Value = "Σήμερα";
                }
                else if (date == today.AddDays(-1.0))
                {
                    choices[i].Synonyms.Add(choices[i].Value);
                    choices[i].Value = "Χθες";
                }
                else if (date == today.AddDays(1.0))
                {
                    choices[i].Synonyms.Add(choices[i].Value);
                    choices[i].Value = "Αύριο";
                }
            }

            var prompt = ChoiceFactory.SuggestedAction(choices,
                text: "Επίλεξε μία από αυτές ή πληκτρολόγησε κάποια άλλη παρακάτω στη μορφή ηη/μμ (π.χ. 24/4):");
            var repropmt = ChoiceFactory.SuggestedAction(choices,
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
