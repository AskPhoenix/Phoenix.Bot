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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class LecturePreparationComponent : PreparationComponent
    {
        private readonly CourseRepository courseRepository;
        private readonly LectureRepository lectureRepository;

        public LecturePreparationComponent (PhoenixContext phoenixContext)
            : base(BotActionPreparation.LectureSelection) 
        {
            this.courseRepository = new CourseRepository(phoenixContext);
            this.lectureRepository = new LectureRepository(phoenixContext);
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            bool singleCourse = !options.PrepareForUserOrCourse;
            var dateToPrepareFor = options.DateToPrepareFor.Value;
            IEnumerable<Lecture> lectures;
            int[] courseIds = null;
            
            if (singleCourse)
            {
                lectures = lectureRepository.FindMany(options.IdToPrepareFor, dateToPrepareFor.Date, 
                    scheduledOnly: true, withExamsOnly: options.ExamsOnly);
            }
            else
            {
                courseIds = courseRepository.FindForUser(options.IdToPrepareFor, options.UserRoles.Contains(Role.Teacher)).Select(c => c.Id).ToArray();
                lectures = lectureRepository.FindMany(courseIds, dateToPrepareFor.Date,
                    scheduledOnly: true, withExamsOnly: options.ExamsOnly);
            }

            options.Selectables = PreparationComponentHelper.GetSelectables(lectures);

            if (options.Selectables is null || !options.Selectables.Any())
            {
                DateTime closestDate;
                if (singleCourse)
                    closestDate = lectureRepository.FindClosestLectureDates(options.IdToPrepareFor, Tense.Anytime, dateToPrepareFor.Date, 
                        dayRange: 1, scheduledOnly: true, withExamsOnly: options.ExamsOnly).SingleOrDefault();
                else
                    closestDate = lectureRepository.FindClosestLectureDates(courseIds, Tense.Anytime, dateToPrepareFor.Date,
                        dayRange: 1, scheduledOnly: true, withExamsOnly: options.ExamsOnly).SingleOrDefault();

                if (closestDate == default)
                {
                    // It should never enter here
                    await stepContext.Context.SendActivityAsync("Δε βρέθηκε κάποια διάλεξη.");
                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }

                options.Selectables = PreparationComponentHelper.GetSelectables(new DateTime[1] { closestDate });

                string msg = "Δεν υπάρχουν " +
                    (options.ExamsOnly ? "διαγωνίσματα " : "διαλέξεις ") + $"στις {dateToPrepareFor:d/M} " +
                    (singleCourse ? "για αυτό το μάθημα." : ".");
                
                await stepContext.Context.SendActivityAsync($"{msg} Έψαξα όμως για την πιο κοντινή στις {closestDate:d/M}:");
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
