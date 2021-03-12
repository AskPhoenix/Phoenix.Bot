using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Miscellaneous;
using Phoenix.DataHandle.Main.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class ExerciseDialog : ComponentDialog
    {
        private readonly PhoenixContext _phoenixContext;
        private readonly BotState _conversationState;

        private readonly IStatePropertyAccessor<int[]> _selCourseIds;
        private readonly IStatePropertyAccessor<int> _coursesEnrolledNum;

        private static class WaterfallNames
        {
            public const string Lecture         = "StudentExercise_Lecture_WaterfallDialog";
            public const string LecturePast     = "StudentExercise_LecturePast_WaterfallDialog";
            public const string LectureOther    = "StudentExercise_LectureOther_WaterfallDialog";
            public const string Homework        = "StudentExercise_Homework_WaterfallDialog";
        }

        public ExerciseDialog(PhoenixContext phoenixContext, ConversationState conversationState) 
            : base(nameof(ExerciseDialog))
        {
            _phoenixContext = phoenixContext;
            _conversationState = conversationState;

            _selCourseIds = _conversationState.CreateProperty<int[]>("SelCourseIds");
            _coursesEnrolledNum = _conversationState.CreateProperty<int>("CoursesEnrolledNum");

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));

            //English, Spanish, French, Portuguese, Chinese, German, Italian, and Turkish.
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt), null, "fr-fr"));

            AddDialog(new WaterfallDialog(WaterfallNames.Lecture,
                new WaterfallStep[]
                {
                    CourseStepAsync,
                    LectureStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.LecturePast,
                new WaterfallStep[]
                {
                    PastLectureAskStepAsync,
                    PastLectureStepAsync,
                    PastLectureSelectStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.LectureOther,
                new WaterfallStep[]
                {
                    OtherLectureStepAsync,
                    OtherLectureSelectStepAsync,
                    OtherLectureNotFoundStepAsync,
                    OtherLectureAgainStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Homework,
                new WaterfallStep[]
                {
                    HomeworkStepAsync,
                    HomeworkPageStepAsync,
                    HomeworkOtherStepAsync
                }));

            InitialDialogId = WaterfallNames.Lecture;
        }

        protected override Task OnEndDialogAsync(ITurnContext context, DialogInstance instance, DialogReason reason, CancellationToken cancellationToken = default)
        {
            Task.Run(async () => await _selCourseIds.DeleteAsync(context));

            return base.OnEndDialogAsync(context, instance, reason, cancellationToken);
        }

        #region Lecture Waterfall Dialog

        //TODO: REMOVE
        private async Task<DialogTurnResult> CourseStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var today = CalendarExtensions.GreeceLocalTime().Date;
            string userFbId = stepContext.Context.Activity.From.Id;
            string schoolFbId = stepContext.Context.Activity.Recipient.Id;

            var coursesLookup = _phoenixContext.Course.
                Where(c => c.StudentCourse.Any(sc => sc.CourseId == c.Id && sc.Student.AspNetUserLogins.Any(l => l.ProviderKey == userFbId && l.UserId == sc.StudentId))
                    && c.School.FacebookPageId == schoolFbId && today <= c.LastDate.Date).
                Select(c => new { c.Name, c.Id }).
                ToLookup(c => c.Name, c => c.Id).
                ToDictionary(x => x.Key, x => x.ToArray());

            await _coursesEnrolledNum.SetAsync(stepContext.Context, coursesLookup.Count);

            if (coursesLookup.Count == 0)
            {
                await stepContext.Context.SendActivityAsync("Απ' ό,τι φαίνεται δεν έχεις εγγραφεί σε κάποιο μάθημα προς το παρόν.");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            if (coursesLookup.Count == 1)
                return await stepContext.NextAsync(coursesLookup.First().Value, cancellationToken);

            return await stepContext.BeginDialogAsync("", coursesLookup, cancellationToken);
        }

        private async Task<DialogTurnResult> LectureStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = stepContext.Result as int[];
            await _selCourseIds.SetAsync(stepContext.Context, selCourseIds);

            var lecWithHw = _phoenixContext.Lecture.
                Where(l => selCourseIds.Contains(l.CourseId) && l.Exercise.Count > 0);
            if (lecWithHw.Count() == 0)
            {
                await stepContext.Context.SendActivityAsync("Δεν υπάρχουν ακόμα εργασίες" 
                    + ((await _coursesEnrolledNum.GetAsync(stepContext.Context) > 1) ? "για αυτό το μάθημα." : "."));
                await stepContext.Context.SendActivityAsync("Απόλαυσε τον ελέυθερο χρόνο σου! 😎");

                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            var grNow = CalendarExtensions.GreeceLocalTime();
            if (lecWithHw.Any(l => l.EndDateTime >= grNow))
            {
                var nextLec = lecWithHw.
                    Include(l => l.Exercise).
                    Where(l => l.StartDateTime >= grNow).
                    AsEnumerable().
                    Aggregate((nl, l) => l.StartDateTime < nl.StartDateTime ? l : nl);

                bool singular = nextLec.Exercise.Count == 1;
                await stepContext.Context.SendActivityAsync($"{(singular ? "Η" : "Οι")} εργασί{(singular ? "α" : "ες")} με την κοντινότερη προθεσμία " +
                    $"είναι για τις {nextLec.StartDateTime:m} και είναι {(singular ? "η" : "οι")} παρακάτω:");

                return await stepContext.BeginDialogAsync(WaterfallNames.Homework, nextLec.Id, cancellationToken);
            }

            return await stepContext.BeginDialogAsync(WaterfallNames.LecturePast, null, cancellationToken);
        }

        #endregion

        #region Lecture Past Waterfall Dialog

        private async Task<DialogTurnResult> PastLectureAskStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            await stepContext.Context.SendActivityAsync("Δεν έχεις νέες εργασίες για αυτό το μάθημα!");

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να δεις παλαιότερες εργασίες σου;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });
        }

        private async Task<DialogTurnResult> PastLectureStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);

            if ((stepContext.Result as FoundChoice).Index == 1)
            {
                await stepContext.Context.SendActivityAsync("Εντάξει! Όποτε θέλεις μπορείς να ελέγξεις ξανά για νέες εργασίες! 😊");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            var grNow = CalendarExtensions.GreeceLocalTime();

            var pLecDates = _phoenixContext.Lecture.
                Where(l => selCourseIds.Contains(l.CourseId) && l.StartDateTime < grNow && l.Exercise.Count > 0).
                OrderByDescending(h => h.StartDateTime).
                Take(5).
                Select(l => $"{l.StartDateTime.Day}/{l.StartDateTime.Month}").
                ToArray();

            await stepContext.Context.SendActivityAsync("Ωραία! Παρακάτω θα βρεις μερικές από τις πιο πρόσφατες ημερομηνίες που είχες μάθημα.");

            var prompt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(pLecDates), 
                text: "Επίλεξε μία από αυτές ή πληκτρολόγησε κάποια άλλη παρακάτω:");
            var repropmt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(pLecDates),
                text: "Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ημέρα/μήνας (π.χ. 24/4)):");

            return await stepContext.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = (Activity)prompt,
                    RetryPrompt = (Activity)repropmt
                });
        }

        private async Task<DialogTurnResult> PastLectureSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(WaterfallNames.LectureOther, stepContext.Result, cancellationToken);

        #endregion

        #region Homework Waterfall Dialog

        private async Task<DialogTurnResult> HomeworkStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);
            int lecId = Convert.ToInt32(stepContext.Options);
            var lec = _phoenixContext.Lecture.Include(l => l.Course).Single(l => l.Id == lecId);
            
            string courseName = lec.Course.Name;
            string subCourse = lec.Course.SubCourse;

            var pageAcsr = _conversationState.CreateProperty<int>("HomeworkPage");
            int page = await pageAcsr.GetAsync(stepContext.Context);

            string fbId = stepContext.Context.Activity.From.Id;
            var lecDate = lec.StartDateTime;
            bool forPastLec = lecDate < CalendarExtensions.GreeceLocalTime();
            decimal? grade = null;
            const int pageSize = 3;

            var paginatedHw = _phoenixContext.Exercise.
                Include(h => h.Book).
                Where(h => h.LectureId == lecId).
                AsEnumerable().
                Where((_, i) => i >= pageSize * page && i < pageSize * (page + 1));

            int hwShownCount = page * pageSize;
            foreach (var hw in paginatedHw)
            {
                var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2));
                card.BackgroundImage = new AdaptiveBackgroundImage("https://www.bot.askphoenix.gr/assets/4f5d75_sq.png");
                card.Body.Add(new AdaptiveTextBlockHeaderLight($"Εργασία {++hwShownCount}η - {lecDate:dddd} {lecDate.Day}/{lecDate.Month}"));
                card.Body.Add(new AdaptiveTextBlockHeaderLight(courseName + (subCourse != null ? $" - {subCourse}" : "")));
                if (hw.Book != null)
                    card.Body.Add(new AdaptiveRichFactSetLight("Βιβλίο ", hw.Book.Name));
                if (hw.Page != null)
                    card.Body.Add(new AdaptiveRichFactSetLight("Σελίδα ", hw.Page, separator: true));
                if (forPastLec)
                {
                    grade = _phoenixContext.StudentExercise.
                        SingleOrDefault(se => se.ExerciseId == hw.Id && se.Student.AspNetUserLogins.Any(l => l.ProviderKey == fbId && l.UserId == se.StudentId))?.
                        Grade;
                    card.Body.Add(new AdaptiveRichFactSetLight("Βαθμός ", grade == null ? "-" : grade.ToString(), separator: true));
                }
                card.Body.Add(new AdaptiveRichFactSetLight("Άσκηση ", hw.Name, separator: true));
                card.Body.Add(new AdaptiveRichFactSetLight("Σχόλια ", string.IsNullOrEmpty(hw.Comments) ? "-" : hw.Comments, separator: true));

                await stepContext.Context.SendActivityAsync(
                    MessageFactory.Attachment(new Attachment(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card))));
            }

            int hwCount = _phoenixContext.Exercise.Count(h => h.LectureId == lecId);
            if (pageSize * (page + 1) < hwCount)
            {
                int hwLeft = hwCount - (pageSize * page + paginatedHw.Count());
                int showMoreNum = hwLeft <= pageSize ? hwLeft : pageSize;
                bool singular = hwLeft == 1;

                await pageAcsr.SetAsync(stepContext.Context, page + 1);

                string showMoreNumEmoji = string.Empty;
                foreach (var digit in showMoreNum.GetDigitsArray())
                    showMoreNumEmoji += digit.ToString() + "\ufe0f\u20e3";

                return await stepContext.PromptAsync(
                    nameof(UnaccentedChoicePrompt),
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text($"Υπάρχ{(singular ? "ει" : "ουν")} ακόμη {hwLeft} εργασί{(singular ? "α" : "ες")} " +
                            $"για τις {lecDate:m}."),
                        RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω απαντήσεις:"),
                        Choices = new Choice[] { new Choice($"Εμφάνιση {showMoreNumEmoji} ακόμη"), new Choice("🔚 Ολοκλήρωση") }
                    });
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> HomeworkPageStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is FoundChoice foundChoice && foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Homework, stepContext.Options, cancellationToken);

            await _conversationState.CreateProperty<int>("HomeworkPage").DeleteAsync(stepContext.Context);

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να δεις εργασίες για άλλη ημερομηνία;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });
        }

        private async Task<DialogTurnResult> HomeworkOtherStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(WaterfallNames.LectureOther, stepContext.Result, cancellationToken);

        #endregion

        #region Lecture Other Waterfall Dialog

        private async Task<DialogTurnResult> OtherLectureStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Options is IList<DateTimeResolution>)
                return await stepContext.NextAsync(stepContext.Options, cancellationToken);

            if (stepContext.Options is FoundChoice foundChoice && foundChoice.Index == 1)
            {
                await stepContext.Context.SendActivityAsync("ΟΚ! 😊");
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);

            var grNow = CalendarExtensions.GreeceLocalTime();
            var lecDates = _phoenixContext.Lecture.
                    Where(l => selCourseIds.Contains(l.CourseId) && l.Exercise.Count > 0).
                    Select(l => l.StartDateTime).
                    OrderByDescending(d => d).
                    Take(5).
                    Select(d => $"{d.Day}/{d.Month}").
                    ToList();

            var prompt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(lecDates),
                text: "Επίλεξε μία από τις παρακάτω ημερομηνίες ή γράψε μια άλλη:");
            var reprompt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(lecDates),
                text: "Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ημέρα/μήνας (π.χ. 24/4)):");

            return await stepContext.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = (Activity)prompt,
                    RetryPrompt = (Activity)reprompt
                });
        }

        private async Task<DialogTurnResult> OtherLectureSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);

            var selDate = CalendarExtensions.ResolveDateTime(stepContext.Result as IList<DateTimeResolution>);
            var lec = _phoenixContext.Lecture.
                Include(l => l.Exercise).
                FirstOrDefault(l => selCourseIds.Contains(l.CourseId) && l.StartDateTime.Date == selDate.Date);

            if (lec == null)
            {
                lec = _phoenixContext.Lecture.
                    Where(l => selCourseIds.Contains(l.CourseId) && l.Exercise.Count > 0).
                    AsEnumerable().
                    Aggregate((l, cl) => Math.Abs((l.StartDateTime - selDate).Days) < Math.Abs((cl.StartDateTime - selDate).Days) ? l : cl);

                await stepContext.Context.SendActivityAsync($"Δεν υπάρχει διάλεξη για αυτό το μάθημα στις {selDate:m}.");
                await stepContext.Context.SendActivityAsync($"Βρήκα όμως για την πιο κοντινή της στις {lec.StartDateTime:m}:");

                return await stepContext.ReplaceDialogAsync(WaterfallNames.Homework, lec.Id, cancellationToken);
            }

            int hwCount = lec.Exercise.Count;
            if (hwCount == 0)
            {
                await stepContext.Context.SendActivityAsync($"Δεν υπάρχουν εργασίες για τις {selDate:m}");
                return await stepContext.NextAsync(null, cancellationToken);
            }

            bool isPastLec = selDate < CalendarExtensions.GreeceLocalTime();
            bool singular = hwCount == 1;
            await stepContext.Context.SendActivityAsync($"Για τις {selDate:m} {(isPastLec ? "είχες" : "έχεις")} τ{(singular ? "ην" : "ις")} " +
                $"παρακάτω εργασί{(singular ? "α" : "ες")}:");
            return await stepContext.ReplaceDialogAsync(WaterfallNames.Homework, lec.Id, cancellationToken);
        }

        private async Task<DialogTurnResult> OtherLectureNotFoundStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να δοκιμάσεις ξανά με άλλη ημερομηνία;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });

        private async Task<DialogTurnResult> OtherLectureAgainStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
            => await stepContext.ReplaceDialogAsync(WaterfallNames.LectureOther, stepContext.Result, cancellationToken);

        #endregion
    }
}
