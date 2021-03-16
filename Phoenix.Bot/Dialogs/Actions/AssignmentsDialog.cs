using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class AssignmentsDialog : ComponentDialog
    {
        public AssignmentsDialog()
            : base(nameof(AssignmentsDialog))
        {
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Assignments.Homework,
                new WaterfallStep[]
                {
                    //FindHomeworkStepAsync,
                    //ShowHomeworkStepAsync,
                    //PageHomeworkStepAsync
                }));

            InitialDialogId = WaterfallNames.Actions.Assignments.Homework;
        }

        private Task<DialogTurnResult> FindHomeworkStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        //#region Homework Waterfall Dialog

        //private async Task<DialogTurnResult> HomeworkStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        //{
        //    var pageAcsr = _conversationState.CreateProperty<int>("HomeworkPage");
        //    int page = await pageAcsr.GetAsync(stepContext.Context);

        //    string fbId = stepContext.Context.Activity.From.Id;
        //    var lecDate = lec.StartDateTime;
        //    bool forPastLec = lecDate < CalendarExtensions.GreeceLocalTime();
        //    decimal? grade = null;
        //    const int pageSize = 3;

        //    var paginatedHw = _phoenixContext.Exercise.
        //        Include(h => h.Book).
        //        Where(h => h.LectureId == lecId).
        //        AsEnumerable().
        //        Where((_, i) => i >= pageSize * page && i < pageSize * (page + 1));

        //    int hwShownCount = page * pageSize;
        //    foreach (var hw in paginatedHw)
        //    {
        //        var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2));
        //        card.BackgroundImage = new AdaptiveBackgroundImage("https://www.bot.askphoenix.gr/assets/4f5d75_sq.png");
        //        card.Body.Add(new AdaptiveTextBlockHeaderLight($"Εργασία {++hwShownCount}η - {lecDate:dddd} {lecDate.Day}/{lecDate.Month}"));
        //        card.Body.Add(new AdaptiveTextBlockHeaderLight(courseName + (subCourse != null ? $" - {subCourse}" : "")));
        //        if (hw.Book != null)
        //            card.Body.Add(new AdaptiveRichFactSetLight("Βιβλίο ", hw.Book.Name));
        //        if (hw.Page != null)
        //            card.Body.Add(new AdaptiveRichFactSetLight("Σελίδα ", hw.Page, separator: true));
        //        if (forPastLec)
        //        {
        //            grade = _phoenixContext.StudentExercise.
        //                SingleOrDefault(se => se.ExerciseId == hw.Id && se.Student.AspNetUserLogins.Any(l => l.ProviderKey == fbId && l.UserId == se.StudentId))?.
        //                Grade;
        //            card.Body.Add(new AdaptiveRichFactSetLight("Βαθμός ", grade == null ? "-" : grade.ToString(), separator: true));
        //        }
        //        card.Body.Add(new AdaptiveRichFactSetLight("Άσκηση ", hw.Name, separator: true));
        //        card.Body.Add(new AdaptiveRichFactSetLight("Σχόλια ", string.IsNullOrEmpty(hw.Comments) ? "-" : hw.Comments, separator: true));

        //        await stepContext.Context.SendActivityAsync(
        //            MessageFactory.Attachment(new Attachment(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card))));
        //    }

        //    int hwCount = _phoenixContext.Exercise.Count(h => h.LectureId == lecId);
        //    if (pageSize * (page + 1) < hwCount)
        //    {
        //        int hwLeft = hwCount - (pageSize * page + paginatedHw.Count());
        //        int showMoreNum = hwLeft <= pageSize ? hwLeft : pageSize;
        //        bool singular = hwLeft == 1;

        //        await pageAcsr.SetAsync(stepContext.Context, page + 1);

        //        string showMoreNumEmoji = string.Empty;
        //        foreach (var digit in showMoreNum.GetDigitsArray())
        //            showMoreNumEmoji += digit.ToString() + "\ufe0f\u20e3";

        //        return await stepContext.PromptAsync(
        //            nameof(UnaccentedChoicePrompt),
        //            new PromptOptions
        //            {
        //                Prompt = MessageFactory.Text($"Υπάρχ{(singular ? "ει" : "ουν")} ακόμη {hwLeft} εργασί{(singular ? "α" : "ες")} " +
        //                    $"για τις {lecDate:m}."),
        //                RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω απαντήσεις:"),
        //                Choices = new Choice[] { new Choice($"Εμφάνιση {showMoreNumEmoji} ακόμη"), new Choice("🔚 Ολοκλήρωση") }
        //            });
        //    }

        //    return await stepContext.NextAsync(null, cancellationToken);
        //}

        //private async Task<DialogTurnResult> HomeworkPageStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        //{
        //    if (stepContext.Result is FoundChoice foundChoice && foundChoice.Index == 0)
        //        return await stepContext.ReplaceDialogAsync(WaterfallNames.Homework, stepContext.Options, cancellationToken);

        //    await _conversationState.CreateProperty<int>("HomeworkPage").DeleteAsync(stepContext.Context);

        //    return await stepContext.PromptAsync(
        //        nameof(UnaccentedChoicePrompt),
        //        new PromptOptions
        //        {
        //            Prompt = MessageFactory.Text("Θα ήθελες να δεις εργασίες για άλλη ημερομηνία;"),
        //            RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
        //            Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
        //        });
        //}

        //private async Task<DialogTurnResult> HomeworkOtherStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        //    => await stepContext.ReplaceDialogAsync(WaterfallNames.LectureOther, stepContext.Result, cancellationToken);

        //#endregion

    }
}
