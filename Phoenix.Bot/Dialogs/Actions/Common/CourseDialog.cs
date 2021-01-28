using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Linguistic;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions.Common
{
    public class CourseDialog : ComponentDialog
    {
        private static class WaterfallNames
        {
            public const string Course = "Student_CommonCourse_WaterfallDialog";
        }

        private static readonly string[] BookEmojis = new string[4] { "📕", "📗", "📘", "📙" };

        public CourseDialog() :
            base(nameof(CourseDialog))
        {
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Course,
                new WaterfallStep[]
                {
                    CourseStepAsync,
                    CourseSelectStepAsync
                }));

            InitialDialogId = WaterfallNames.Course;
        }

        #region Course Waterfall Dialog

        private async Task<DialogTurnResult> CourseStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var coursesLookup = stepContext.Options as Dictionary<string, int[]>;
            var parentId = stepContext.Parent.Stack[1].Id;
            string topic = parentId.StartsWith("StudentExercise") ? "τις εργασίες" : "τα διαγωνίσματά";

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text($"Για ποιο μάθημα θα ήθελες να δεις {topic} σου;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκτρολόγησε ένα από τα παρακάτω μαθήματα:"),
                    Choices = ChoiceFactory.ToChoices(coursesLookup.Select((p, i) => BookEmojis[i % 4] + " " + p.Key).ToList())
                }, cancellationToken);
        }

        private async Task<DialogTurnResult> CourseSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            string resValue = (stepContext.Result as FoundChoice).Value;
            var selCourseName = resValue.TrimEmojis();
            var coursesLookup = stepContext.Options as Dictionary<string, int[]>;

            int[] selCourseIds = coursesLookup[selCourseName];

            return await stepContext.EndDialogAsync(selCourseIds, cancellationToken);
        }

        #endregion
    }
}
