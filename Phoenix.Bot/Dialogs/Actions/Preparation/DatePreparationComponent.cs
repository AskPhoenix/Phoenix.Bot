using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.Miscellaneous;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Collections.Generic;
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
            IEnumerable<DateTime> dates;

            if (singleCourse)   // Student --> Search, Grades
            {
                //TODO: Check if exams only attribulte works properly
                dates = lectureRepository.FindClosestLectureDates(options.IdToPrepareFor, Tense.Past, 
                    scheduledOnly: true, withExamsOnly: options.ExamsOnly);
            }
            else                // Teacher --> Assignments, Grades
            {
                int[] courseIds = courseRepository.FindForUser(options.IdToPrepareFor, options.UserRole == Role.Teacher).Select(c => c.Id).ToArray();
                dates = lectureRepository.FindClosestLectureDates(courseIds, Tense.Anytime, 
                    scheduledOnly: true, withExamsOnly: options.ExamsOnly);
            }

            options.Selectables = PreparationComponentHelper.GetSelectables(dates);

            if (options.Selectables is null || !options.Selectables.Any())
            {
                string msg = "Δεν υπάρχουν ακόμα " +
                    (options.ExamsOnly ? "διαγωνίσματα " : "διαλέξεις ") + "για " +
                    (singleCourse ? "αυτό το " : "κάποιο ") + "μάθημα.";

                await stepContext.Context.SendActivityAsync(msg);
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }
            else if (options.Selectables.Count == 1)
                return await stepContext.EndDialogAsync(CalendarExtensions.ParseDate(options.Selectables.Single().Value), cancellationToken);

            return await stepContext.NextAsync(null, cancellationToken);
        }

        protected override async Task<DialogTurnResult> AskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            bool singleCourse = !options.PrepareForUserOrCourse;

            var choices = ChoiceFactory.ToChoices(options.Selectables.Values.ToList());
            //TODO: Χρήση offset για την εύρεση του σήμερα
            var today = DateTimeOffset.UtcNow.Date;

            for (int i = 0; i < choices.Count; i++)
            {
                var date = CalendarExtensions.ParseDate(choices[i].Value).Date;
                if (date == today)
                    choices[i].Value = "Σήμερα";
                else if (date == today.AddDays(-1.0))
                    choices[i].Value = "Χθες";
                else if (date == today.AddDays(1.0))
                    choices[i].Value = "Αύριο";
            }

            //await stepContext.Context.SendActivityAsync("Για ποια μέρα θα ήθελες να δεις τις εργασίες για το σπίτι;");
            var prompt = ChoiceFactory.SuggestedAction(choices, text: "Επίλεξε μία από τις παρακάτω κοντινές ημερομηνίες ή γράψε κάποια άλλη:");
            var repropmt = ChoiceFactory.SuggestedAction(choices, text: "Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ημέρα/μήνας:");

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
            var msg = stepContext.Context.Activity.Text;
            var res = stepContext.Result as IList<DateTimeResolution>;

            var resolvedDate = CalendarExtensions.ResolveDateTimePromptResult(res, msg);

            return await stepContext.EndDialogAsync(resolvedDate, cancellationToken);
        }
    }
}
