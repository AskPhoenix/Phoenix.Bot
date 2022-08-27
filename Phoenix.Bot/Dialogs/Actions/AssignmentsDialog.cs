using AdaptiveCards;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.State.Options.Actions;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class AssignmentsDialog : StateDialog
    {
        private readonly LectureRepository _lectureRepository;

        public AssignmentsDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, nameof(AssignmentsDialog))
        {
            _lectureRepository = new(phoenixContext, nonObviatedOnly: true);

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Assignments.Homework,
                new WaterfallStep[]
                {
                    HomeworkStepAsync
                }));

            InitialDialogId = WaterfallNames.Actions.Assignments.Homework;
        }

        #region Homework Waterfall Dialog

        private async Task<DialogTurnResult> HomeworkStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (AssignmentsOptions)stepCtx.Options;

            IEnumerable<Exercise> exercises = Enumerable.Empty<Exercise>();
            IEnumerable<Exam> exams = Enumerable.Empty<Exam>();

            var now = CalendarExtensions.TimeZoneNow(CData.School.SchoolSetting.TimeZone);
            var dateToPrepareFor = options.DateToPrepareFor ?? now.Date;

            if (options.LectureId.HasValue)     // Students' Search or Teachers' Assignments
            {
                var lecture = await _lectureRepository.FindPrimaryAsync(options.LectureId.Value, canTkn);
                if (lecture is null)
                {
                    await stepCtx.Context.SendActivityAsync($"Δε βρέθηκε διάλεξη για τις {dateToPrepareFor}.");
                    return await stepCtx.EndDialogAsync(null, canTkn);
                }

                exercises = lecture.Exercises;

                if (!options.Search)            // Find past exams only for Teachers
                {
                    exams = lecture.Exams;

                    if (!exercises.Any() && !exams.Any())
                    {
                        await stepCtx.Context.SendActivityAsync($"Δε βρέθηκαν εργασίες, ούτε διαγωνίσματα για τις {dateToPrepareFor:d/M}.");
                        return await stepCtx.EndDialogAsync(null, canTkn);
                    }
                }
                else
                {
                    if (!exercises.Any())
                    {
                        await stepCtx.Context.SendActivityAsync($"Δε βρέθηκαν εργασίες για τις {dateToPrepareFor:d/M}.");
                        return await stepCtx.EndDialogAsync(null, canTkn);
                    }
                }
            }
            else                                // Students' Assignments
            {
                var lectures = UData.PhoenixUser!
                    .Courses
                    .SelectMany(c => c.Lectures)
                    .Where(l => l.StartDateTime >= now);

                if (!lectures.Any())
                {
                    await stepCtx.Context.SendActivityAsync($"Δε βρέθηκαν διαλέξεις.");
                    return await stepCtx.EndDialogAsync(null, canTkn);
                }

                exercises = lectures.SelectMany(l => l.Exercises);
                exams = lectures.SelectMany(l => l.Exams);

                if (!exercises.Any() && !exams.Any())
                {
                    await stepCtx.Context.SendActivityAsync("Δεν υπάρχουν ακόμα εργασίες για το επόμενο μάθημα.");
                    if (!options.AffiliatedUserId.HasValue)
                        await stepCtx.Context.SendActivityAsync("Απόλαυσε τον ελεύθερο χρόνο σου! 😎");

                    return await stepCtx.EndDialogAsync(null, canTkn);
                }
            }

            string msg = options.LectureId.HasValue ? $"Για τις {dateToPrepareFor:d/M} βρέθκαν:" : "Για την επόμενη φορά βρέθηκαν:";
            if (exercises.Any())
                msg += $"\n  •  {exercises.Count()} εργασίες";
            if (exams.Any())
                msg += $"\n  •  {exams.Count()} διαγωνίσματα";

            var adaptiveCards = new List<AdaptiveCard>(exercises.Count() + exams.Count());
            adaptiveCards.AddRange(await GetCardsAsync(exercises));
            adaptiveCards.AddRange(await GetCardsAsync(exams));

            var attachments = adaptiveCards.Select(
                c =>new Attachment(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(c)));
            await stepCtx.Context.SendActivityAsync(MessageFactory.Attachment(attachments, text: msg));

            return await stepCtx.EndDialogAsync(null, canTkn);
        }

        private async Task<List<AdaptiveCard>> GetCardsAsync(IEnumerable<Exercise> exercises)
        {
            var cards = new List<AdaptiveCard>();

            if (exercises.Any())
            {
                var exerciseGroups = exercises.GroupBy(e => e.LectureId);
                int groupsNum = exerciseGroups.Count();

                foreach (var group in exerciseGroups)
                {                    
                    var lecture = await _lectureRepository.FindPrimaryAsync(group.Key);

                    

                    string title = groupsNum == 1 ? "Εργασία" : "Εργασίες";
                    string courseFullName = lecture!.Course.GetNameWithSubcourse();

                    var card = new AdaptivePhoenixCard(new AdaptiveTextBlockHeaderLight[]
                    {
                        new($"{title}: {courseFullName}"),
                        new($"{lecture.StartDateTime:g}")
                    });

                    int i = 1;
                    foreach (var exercise in group)
                    {
                        if (groupsNum > 1)
                            card.Body.Add(new AdaptiveTextBlockHeaderLight($"Εργασία {i++}") { Size = AdaptiveTextSize.Large });
                        if (exercise.Book != null)
                            card.Body.Add(new AdaptiveRichFactSetLight("Βιβλίο ", exercise.Book.Name));
                        if (!string.IsNullOrWhiteSpace(exercise.Page))
                            card.Body.Add(new AdaptiveRichFactSetLight("Σελίδα ", exercise.Page, separator: true));
                        if (lecture.StartDateTime < DateTimeOffset.UtcNow)
                        {
                            var grade = exercise.Grades.FirstOrDefault();
                            card.Body.Add(new AdaptiveRichFactSetLight("Βαθμός ", grade is null ? "-" : grade.Score.ToString(), separator: true));
                        }
                        card.Body.Add(new AdaptiveRichFactSetLight("Άσκηση ", exercise.Name, separator: true));
                        card.Body.Add(new AdaptiveRichFactSetLight("Σχόλια ", string.IsNullOrWhiteSpace(exercise.Comments) ? "-" : exercise.Comments, separator: true));
                    }
                    
                    cards.Add(card);
                }
            }

            return cards;
        }

        private async Task<List<AdaptiveCard>> GetCardsAsync(IEnumerable<Exam> exams)
        {
            var cards = new List<AdaptiveCard>();

            if (exams.Any())
            {
                var examGroups = exams.GroupBy(e => e.LectureId);
                int groupsNum = examGroups.Count();

                foreach (var group in examGroups)
                {
                    var lecture = await _lectureRepository.FindPrimaryAsync(group.Key);

                    string title = groupsNum == 1 ? "Διαγώνισμα" : "Διαγωνίσματα";
                    string courseFullName = lecture!.Course.GetNameWithSubcourse();

                    var card = new AdaptivePhoenixCard(new AdaptiveTextBlockHeaderLight[]
                    {
                        new($"{title}: {courseFullName}"),
                        new($"{lecture.StartDateTime:g}")
                    });

                    int i = 1;
                    foreach (var exam in group)
                    {
                        if (groupsNum > 1)
                            card.Body.Add(new AdaptiveTextBlockHeaderLight($"Διαγώνισμα {i++}") { Size = AdaptiveTextSize.Large });

                        int j = 1;
                        foreach (var material in exam.Materials)
                        {
                            if (exam.Materials.Count > 1)
                                card.Body.Add(new AdaptiveTextBlockHeaderLight($"Ύλη {j++}") { Size = AdaptiveTextSize.Large });
                            if (material.Book != null)
                                card.Body.Add(new AdaptiveRichFactSetLight("Βιβλίο ", material.Book.Name));
                            if (!string.IsNullOrWhiteSpace(material.Chapter))
                                card.Body.Add(new AdaptiveRichFactSetLight("Κεφάλαιο ", material.Chapter, separator: true));
                            if (!string.IsNullOrWhiteSpace(material.Section))
                                card.Body.Add(new AdaptiveRichFactSetLight("Ενότητα ", material.Section, separator: true));
                            card.Body.Add(new AdaptiveRichFactSetLight("Σχόλια ", string.IsNullOrWhiteSpace(material.Comments) ? "-" : material.Comments, separator: true));
                        }
                    }
                    cards.Add(card);
                }
            }

            return cards;
        }

        #endregion
    }
}
