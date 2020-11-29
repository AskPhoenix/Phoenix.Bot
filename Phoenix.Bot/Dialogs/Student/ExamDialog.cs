using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Dialogs.Student.Common;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.Miscellaneous;
using Phoenix.DataHandle.Main.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Student
{
    public class ExamDialog : ComponentDialog
    {
        private readonly PhoenixContext _phoenixContext;
        private readonly BotState _conversationState;

        private readonly IStatePropertyAccessor<int[]> _selCourseIds;
        private readonly IStatePropertyAccessor<int> _coursesEnrolledNum;

        private static class WaterfallNames
        {
            public const string Main        = "StudentExam_Main_WaterfallDialog";
            public const string FutureExam  = "StudentExam_FutureExam_WaterfallDialog";
            public const string Material    = "StudentExam_Material_WaterfallDialog";
            public const string PastExam    = "StudentExam_PastExam_WaterfallDialog";
            public const string Grade       = "StudentExam_Grade_WaterfallDialog";
        }

        public ExamDialog(PhoenixContext phoenixContext, ConversationState conversationState)
            : base(nameof(ExamDialog))
        {
            _phoenixContext = phoenixContext;
            _conversationState = conversationState;

            _selCourseIds = _conversationState.CreateProperty<int[]>("SelCourseIds");
            _coursesEnrolledNum = _conversationState.CreateProperty<int>("CoursesEnrolledNum");

            AddDialog(new CourseDialog());
            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt), null, "fr-fr"));

            AddDialog(new WaterfallDialog(WaterfallNames.Main,
                new WaterfallStep[]
                {
                    CourseStepAsync,
                    ExamStepAsync,
                    RedirectStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.FutureExam,
                new WaterfallStep[]
                {
                    FollowingExamStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Material,
                new WaterfallStep[]
                {
                    MaterialStepAsync,
                    MaterialPageStepAsync,
                    MaterialOtherStepAsync,
                    MaterialOtherSelectStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.PastExam,
                new WaterfallStep[]
                {
                    PreviousExamStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Grade,
                new WaterfallStep[]
                {
                    GradeStepAsync,
                    GradeOtherStepAsync,
                    GradeOtherSelectStepAsync
                }));

            InitialDialogId = WaterfallNames.Main;
        }

        protected override Task OnEndDialogAsync(ITurnContext context, DialogInstance instance, DialogReason reason, CancellationToken cancellationToken = default)
        {
            Task.Run(async () => await _selCourseIds.DeleteAsync(context));

            return base.OnEndDialogAsync(context, instance, reason, cancellationToken);
        }

        #region Main Waterfall Dialog

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

            return await stepContext.BeginDialogAsync(nameof(CourseDialog), coursesLookup, cancellationToken);
        }

        private async Task<DialogTurnResult> ExamStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = stepContext.Result as int[];
            await _selCourseIds.SetAsync(stepContext.Context, selCourseIds);
            string fbId = stepContext.Context.Activity.From.Id;
            var exams = _phoenixContext.Exam.Where(e => selCourseIds.Contains(e.Lecture.CourseId));

            bool anyExams = exams.Any();
            bool anyFutureExams = exams.Any(e => e.Lecture.EndDateTime >= CalendarExtensions.GreeceLocalTime());
            bool anyGradedExams = exams.Any(e => e.StudentExam.Any(se => se.Student.AspNetUserLogins.Any(l => l.ProviderKey == fbId && l.UserId == se.StudentId) && se.Exam.Id == e.Id && se.Grade != null));

            if (!anyExams)
            {
                await stepContext.Context.SendActivityAsync("Δεν υπάρχουν ακόμα εργασίες"
                    + ((await _coursesEnrolledNum.GetAsync(stepContext.Context) > 1) ? "για αυτό το μάθημα." : "."));
                await stepContext.Context.SendActivityAsync("Απόλαυσε τον ελέυθερο χρόνο σου! 😎");

                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            if (!anyFutureExams && !anyGradedExams)
            {
                await stepContext.Context.SendActivityAsync("Δεν υπάρχουν προγραμματισμένα διαγωνίσματα, ούτε έχουν βγει βαθμοί για αυτό το μάθημα.");

                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            if (!anyGradedExams)
            {
                if (!exams.Any(e => e.Lecture.EndDateTime < CalendarExtensions.GreeceLocalTime()))
                    await stepContext.Context.SendActivityAsync("Δεν έχουν βγει ακόμα βαθμοί για κάποιο διαγώνισμα.");

                return await stepContext.NextAsync(0, cancellationToken);
            }
            if (!anyFutureExams)
            {
                await stepContext.Context.SendActivityAsync("Δεν υπάρχει κάποιο προγραμματισμένο διαγώνισμα προς το παρόν.");

                return await stepContext.NextAsync(1, cancellationToken);
            }

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να δεις την ύλη για επόμενα διαγωνίσματα ή τους βαθμούς για παλαιότερα;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε ή πληκρολόγησε μία από τις παρακάτω επιλογές:"),
                    Choices = new Choice[] { new Choice("📄 Ύλη"), new Choice("💯 Βαθμοί") }
                });
        }

        private async Task<DialogTurnResult> RedirectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int choice = stepContext.Result is int res ? res : (stepContext.Result as FoundChoice).Index;

            return choice == 0 ? 
               await stepContext.BeginDialogAsync(WaterfallNames.FutureExam, null, cancellationToken) :
               await stepContext.BeginDialogAsync(WaterfallNames.PastExam, null, cancellationToken);
        }

        #endregion

        #region Future Exam Waterfall Dialog

        private async Task<DialogTurnResult> FollowingExamStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);

            var nextExam = _phoenixContext.Exam.
                Include(e => e.Lecture.Classroom).
                Where(e => selCourseIds.Contains(e.Lecture.CourseId) && e.Lecture.EndDateTime >= CalendarExtensions.GreeceLocalTime()).
                AsEnumerable().
                Aggregate((e, ne) => e.Lecture.StartDateTime < ne.Lecture.StartDateTime ? e : ne);

            string dayArticle = nextExam.Lecture.StartDateTime.DayOfWeek == DayOfWeek.Saturday ? "το" : "την";
            await stepContext.Context.SendActivityAsync($"Το αμέσως επόμενο διαγώνισμα είναι {dayArticle} {nextExam.Lecture.StartDateTime:dddd} " +
                $"{nextExam.Lecture.StartDateTime:m} στις {nextExam.Lecture.StartDateTime:t}" + (nextExam.Lecture?.ClassroomId != null ? $" στην αίθουσα {nextExam.Lecture.Classroom.Name}." : "."));

            return await stepContext.ReplaceDialogAsync(WaterfallNames.Material, nextExam.Id, cancellationToken);
        }

        #endregion

        #region Material Waterfall Dialog

        //Shows Material only for upcoming exams
        private async Task<DialogTurnResult> MaterialStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);
            int examId = Convert.ToInt32(stepContext.Options);
            var exam = _phoenixContext.Exam.
                Include(e => e.Lecture.Course).
                Single(e => e.Id == examId);

            var examDate = exam.Lecture.StartDateTime;
            var courseName = exam.Lecture.Course.Name;
            var subCourse = exam.Lecture.Course.SubCourse;

            var pageAcsr = _conversationState.CreateProperty<int>("MaterialPage");
            int page = await pageAcsr.GetAsync(stepContext.Context);
            const int pageSize = 3;

            var paginatedMat = _phoenixContext.Material.
                Include(m => m.Book).                
                Where(m => m.ExamId == examId).
                AsEnumerable().
                Where((_, i) => i >= pageSize * page && i < pageSize * (page + 1));

            int matShownCount = page * pageSize;
            foreach (var mat in paginatedMat)
            {
                await stepContext.Context.SendActivityAsync(new Activity(type: ActivityTypes.Typing));

                var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2));
                card.BackgroundImage = new AdaptiveBackgroundImage("https://www.bot.askphoenix.gr/assets/4f5d75_sq.png");
                card.Body.Add(new AdaptiveTextBlockHeaderLight($"Ύλη {++matShownCount} - {examDate:dddd} {examDate.Day}/{examDate.Month}"));
                card.Body.Add(new AdaptiveTextBlockHeaderLight(courseName + (subCourse != null ? $" - {subCourse}" : "")));
                if (mat.Book != null)
                    card.Body.Add(new AdaptiveRichFactSetLight("Βιβλίο ", mat.Book.Name));
                if (mat.Chapter != null)
                    card.Body.Add(new AdaptiveRichFactSetLight("Κεφάλαιο ", mat.Chapter, separator: true));
                if (mat.Section != null)
                    card.Body.Add(new AdaptiveRichFactSetLight("Ενότητα ", mat.Section, separator: true));
                card.Body.Add(new AdaptiveRichFactSetLight("Σχόλια ", string.IsNullOrEmpty(mat.Comments) ? "-" : mat.Comments, separator: true));

                await stepContext.Context.SendActivityAsync(
                    MessageFactory.Attachment(new Attachment(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card))));
            }

            int matCount = _phoenixContext.Material.Count(m => m.ExamId == examId);
            if (matShownCount < matCount)
            {
                int matLeft = matCount - matShownCount;
                int showMoreNum = matLeft <= pageSize ? matLeft : pageSize;
                bool singular = matLeft == 1;

                await pageAcsr.SetAsync(stepContext.Context, page + 1);

                string showMoreNumEmoji = string.Empty;
                foreach (var digit in showMoreNum.GetDigitsArray())
                    showMoreNumEmoji += digit.ToString() + "\ufe0f\u20e3";

                return await stepContext.PromptAsync(
                    nameof(UnaccentedChoicePrompt),
                    new PromptOptions
                    {
                        Prompt = MessageFactory.Text($"Υπάρχ{(singular ? "ει" : "ουν")} ακόμη {matLeft} σημεί{(singular ? "ο" : "α")} " +
                            "που πρέπει να διαβάσεις."),
                        RetryPrompt = MessageFactory.Text("Παρακαλώ επίλεξε μία από τις παρακάτω απαντήσεις:"),
                        Choices = new Choice[] { new Choice($"Εμφάνιση {showMoreNumEmoji} ακόμη"), new Choice("Ολοκλήρωση") }
                    });
            }

            return await stepContext.NextAsync(null, cancellationToken);
        }

        private async Task<DialogTurnResult> MaterialPageStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (stepContext.Result is FoundChoice foundChoice && foundChoice.Index == 0)
                return await stepContext.ReplaceDialogAsync(WaterfallNames.Material, stepContext.Options, cancellationToken);

            await _conversationState.CreateProperty<int>("MaterialPage").DeleteAsync(stepContext.Context);

            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);
            string fbId = stepContext.Context.Activity.From.Id;

            if (_phoenixContext.Exam.Count(e => selCourseIds.Contains(e.Lecture.CourseId)) == 1)
            {
                await stepContext.Context.SendActivityAsync("Αυτό ήταν το μόνο διαγώνισμα που έχεις.");
                
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }
            if (_phoenixContext.Exam.Count(e => selCourseIds.Contains(e.Lecture.CourseId) && e.Lecture.EndDateTime >= CalendarExtensions.GreeceLocalTime()) == 1)
            {
                await stepContext.Context.SendActivityAsync("Αυτό ήταν το μόνο προγραμματισμένο διαγώνισμα.");

                return await stepContext.EndDialogAsync(true, cancellationToken);
            }

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να δεις την ύλη για άλλο διαγώνισμα;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });
        }

        private async Task<DialogTurnResult> MaterialOtherStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((stepContext.Result as FoundChoice).Index == 1)
                return await stepContext.EndDialogAsync(null, cancellationToken);

            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);

            var examDates = _phoenixContext.Exam.
                Where(e => selCourseIds.Contains(e.Lecture.CourseId) && e.Lecture.EndDateTime >= CalendarExtensions.GreeceLocalTime()).
                Select(e => e.Lecture.StartDateTime).
                OrderBy(d => d).
                Take(5).
                Select(d => $"{d.Day}/{d.Month}").
                ToList();

            var prompt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(examDates),
                text: "Επίλεξε μία από τις παρακάτω ημερομηνίες ή γράψε μια άλλη:");
            var reprompt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(examDates),
                text: "Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ημέρα/μήνας (π.χ. 24/4)):");

            return await stepContext.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = (Activity)prompt,
                    RetryPrompt = (Activity)reprompt
                });
        }

        private async Task<DialogTurnResult> MaterialOtherSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);

            var selDate = DialogsHelper.ResolveDateTime(stepContext.Result as IList<DateTimeResolution>);
            var exam = _phoenixContext.Exam.
                Include(e => e.Lecture.Classroom).
                FirstOrDefault(e => selCourseIds.Contains(e.Lecture.CourseId) && e.Lecture.StartDateTime.Date == selDate.Date);

            if (exam == null)
            {
                exam = _phoenixContext.Exam.
                    Include(e => e.Lecture.Classroom).
                    Where(e => selCourseIds.Contains(e.Lecture.CourseId) && e.Lecture.EndDateTime > CalendarExtensions.GreeceLocalTime()).
                    AsEnumerable().
                    Aggregate((e, fe) => Math.Abs((e.Lecture.StartDateTime - selDate).Days) < Math.Abs((fe.Lecture.StartDateTime - selDate).Days) ? e : fe);

                await stepContext.Context.SendActivityAsync($"Δεν υπάρχει διαγώνισμα για αυτό το μάθημα στις {selDate:m}.");
                await stepContext.Context.SendActivityAsync($"Βρήκα όμως το πιο κοντινό του στις {exam.Lecture.StartDateTime:m}:");

                return await stepContext.ReplaceDialogAsync(WaterfallNames.Material, exam.Id, cancellationToken);
            }

            string dayArticle = Greek.DayArticle(exam.Lecture.StartDateTime.DayOfWeek);
            await stepContext.Context.SendActivityAsync($"Για το διαγώνισμα {dayArticle} {exam.Lecture.StartDateTime.DayOfWeek} " +
                    $"{exam.Lecture.StartDateTime:m} στις {exam.Lecture.StartDateTime:t}" 
                    + exam.Lecture.ClassroomId != null ? $" στην αίθουσα {exam.Lecture.Classroom.Name} " : " "
                                                                                                           + "έχεις να διαβάσεις τα παρακάτω:");
            return await stepContext.ReplaceDialogAsync(WaterfallNames.Material, exam.Id, cancellationToken);
        }

        #endregion

        #region Past Exam Waterfall Dialog

        private async Task<DialogTurnResult> PreviousExamStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);
            string fbId = stepContext.Context.Activity.From.Id;

            var lastGradedStudentExam = _phoenixContext.StudentExam.
                Include(se => se.Exam).
                Where(se => selCourseIds.Contains(se.Exam.Lecture.CourseId) && se.Student.AspNetUserLogins.Any(l => l.ProviderKey == fbId && l.UserId == se.StudentId) && se.Grade != null).
                AsEnumerable().
                Aggregate((se, lse) => se.Exam.Lecture.StartDateTime > lse.Exam.Lecture.StartDateTime ? se : lse);

            await stepContext.Context.SendActivityAsync($"Το πιο πρόσφατο βαθμολογημένο διαγώνισμα ήταν αυτό στις {lastGradedStudentExam.Exam.Lecture.StartDateTime:m}.");
            await stepContext.Context.SendActivityAsync("Ο βαθμός σου είναι:");

            return await stepContext.ReplaceDialogAsync(WaterfallNames.Grade, lastGradedStudentExam.Grade.Value, cancellationToken);
        }

        #endregion

        #region Grade Waterfall Dialog

        private async Task<DialogTurnResult> GradeStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);
            var grade = Convert.ToDecimal(stepContext.Options);

            if (grade >= 0.0m)
            {
                await stepContext.Context.SendActivityAsync(new Activity(type: ActivityTypes.Typing));

                var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2));
                card.BackgroundImage = new AdaptiveBackgroundImage("https://www.bot.askphoenix.gr/assets/4f5d75_sq.png");
                card.Body.Add(new AdaptiveTextBlockHeaderLight($"--- {grade:G29} ---") { HorizontalAlignment = AdaptiveHorizontalAlignment.Center });
                await stepContext.Context.SendActivityAsync(
                    MessageFactory.Attachment(new Attachment(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(card))));
            }

            if (_phoenixContext.Exam.Count(e => selCourseIds.Contains(e.Lecture.CourseId)) == 1)
            {
                await stepContext.Context.SendActivityAsync("Αυτό ήταν το μόνο διαγώνισμα που είχες.");

                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

            var grNow = CalendarExtensions.GreeceLocalTime();
            string fbId = stepContext.Context.Activity.From.Id;
            if (_phoenixContext.StudentExam.Count(se => se.Student.AspNetUserLogins.Any(l => l.ProviderKey == fbId && l.UserId == se.StudentId) && selCourseIds.Contains(se.Exam.Lecture.CourseId) 
                && se.Exam.Lecture.EndDateTime < grNow && se.Grade != null) == 1)
            {
                await stepContext.Context.SendActivityAsync("Αυτό ήταν το μόνο βαθμολογημένο διαγώνισμα.");

                return await stepContext.EndDialogAsync(true, cancellationToken);
            }

            return await stepContext.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("Θα ήθελες να δεις τον βαθμό σου για άλλο διαγώνισμα;"),
                    RetryPrompt = MessageFactory.Text("Παρακαλώ απάντησε με ένα Ναι ή Όχι:"),
                    Choices = new Choice[] { new Choice("✔️ Ναι"), new Choice("❌ Όχι, ευχαριστώ") { Synonyms = new List<string> { "Όχι" } } }
                });
        }

        private async Task<DialogTurnResult> GradeOtherStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if ((stepContext.Result as FoundChoice).Index == 1)
                return await stepContext.EndDialogAsync(null, cancellationToken);

            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);
            var grNow = CalendarExtensions.GreeceLocalTime();
            string fbId = stepContext.Context.Activity.From.Id;

            var examDates = _phoenixContext.StudentExam.
                Where(se => se.Student.AspNetUserLogins.Any(l => l.ProviderKey == fbId && l.UserId == se.StudentId) && selCourseIds.Contains(se.Exam.Lecture.CourseId) 
                    && se.Exam.Lecture.EndDateTime < grNow && se.Grade != null).
                Select(se => se.Exam.Lecture.StartDateTime).
                OrderByDescending(d => d).
                Take(5).
                Select(d => $"{d.Day}/{d.Month}").
                ToList();

            await stepContext.Context.SendActivityAsync("Εάν δε βλέπεις κάποια ημερομηνία που είχες διαγώνισμα, τότε δεν υπάρχει βαθμός ακόμα.");

            var prompt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(examDates),
                text: "Επίλεξε μία από τις παρακάτω ημερομηνίες ή γράψε μια άλλη:");
            var reprompt = ChoiceFactory.SuggestedAction(ChoiceFactory.ToChoices(examDates),
                text: "Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ημέρα/μήνας (π.χ. 24/4)):");

            return await stepContext.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = (Activity)prompt,
                    RetryPrompt = (Activity)reprompt
                });
        }

        private async Task<DialogTurnResult> GradeOtherSelectStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            int[] selCourseIds = await _selCourseIds.GetAsync(stepContext.Context);
            string fbId = stepContext.Context.Activity.From.Id;

            var selDate = DialogsHelper.ResolveDateTime(stepContext.Result as IList<DateTimeResolution>);
            var gradedStudentExams = _phoenixContext.StudentExam.
                Where(se => selCourseIds.Contains(se.Exam.Lecture.CourseId) && se.Student.AspNetUserLogins.Any(l => l.ProviderKey == fbId && l.UserId == se.StudentId) && se.Grade != null);

            var studentExam = gradedStudentExams.
                SingleOrDefault(se => se.Exam.Lecture.StartDateTime.Date == selDate.Date);
            
            if (studentExam == null)
            {
                if (_phoenixContext.Exam.Any(e => selCourseIds.Contains(e.Lecture.CourseId) && e.Lecture.StartDateTime.Date == selDate.Date))
                {
                    await stepContext.Context.SendActivityAsync($"Το διαγώνισμα για τις {selDate:m} βρίσκεται ακόμα υπό βαθμολόγηση.");

                    return await stepContext.ReplaceDialogAsync(WaterfallNames.Grade, -1.0m, cancellationToken);
                }

                studentExam = gradedStudentExams.
                    Include(se => se.Exam).
                    Where(se => se.Exam.Lecture.EndDateTime < CalendarExtensions.GreeceLocalTime()).
                    AsEnumerable().
                    Aggregate((se, pse) => Math.Abs((se.Exam.Lecture.StartDateTime - selDate).Days) < Math.Abs((pse.Exam.Lecture.StartDateTime - selDate).Days) ? se : pse);

                
                await stepContext.Context.SendActivityAsync($"Δεν υπάρχει διαγώνισμα για αυτό το μάθημα στις {selDate:m}.");
                await stepContext.Context.SendActivityAsync($"Βρήκα όμως το πιο κοντινό του βαθμολογημένο διαγώνισμα στις {studentExam.Exam.Lecture.StartDateTime:m}.");
                await stepContext.Context.SendActivityAsync("Ο βαθμός σου είναι:");

                return await stepContext.ReplaceDialogAsync(WaterfallNames.Grade, studentExam.Grade.Value, cancellationToken);
            }

            await stepContext.Context.SendActivityAsync($"Ο βαθμός σου για το διαγώνισμα στις {selDate:m} είναι:");

            return await stepContext.ReplaceDialogAsync(WaterfallNames.Grade, studentExam.Grade.Value, cancellationToken);
        }

        #endregion
    }
}
