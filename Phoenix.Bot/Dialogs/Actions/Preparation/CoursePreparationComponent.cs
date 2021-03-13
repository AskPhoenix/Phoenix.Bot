﻿using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Linguistic;
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
        private readonly AspNetUserRepository userRepository;

        public CoursePreparationComponent (PhoenixContext phoenixContext)
            : base(BotActionPreparation.CourseSelection) 
        {
            this.userRepository = new AspNetUserRepository(phoenixContext);
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as PreparationComponentOptions;
            var user = await userRepository.Find(options.IdToPrepareFor);

            var courses = options.UserRole.IsStaff()
                ? user.TeacherCourse?.Select(tc => tc.Course)
                : user.StudentCourse?.Select(sc => sc.Course);

            options.Selectables = courses?.
                //GroupBy(c => c.Name.ToUnaccented().ToLower()).        // Distinct Courses by their Name
                //Select(g => g.First()).
                ToDictionary(c => c.Id, tc => tc.NameWithSubcourse);

            if (options.Selectables == null || options.Selectables.Count == 0)
            {
                if (options.IsPreparingForSomeoneElse)
                    await stepContext.Context.SendActivityAsync("Δεν έχουν προστεθεί μαθήματα για τον συγκεκριμένο μαθητή.");
                else
                    await stepContext.Context.SendActivityAsync("Δεν έχεις εγγραφεί ακόμη σε κάποιο μάθημα.");

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
                    Prompt = MessageFactory.Text("Με ποιο μάθημα θα ήθελες να συνεχίσουμε;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε ένα από τα παρακάτω μαθήματα:"),
                    Choices = ChoiceFactory.ToChoices(options.Selectables.Values.ToList())
                },
                cancellationToken);
        }
    }
}
