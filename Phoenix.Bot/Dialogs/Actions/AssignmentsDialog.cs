using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Newtonsoft.Json.Linq;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Main;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class AssignmentsDialog : ComponentDialog
    {
        private readonly ExerciseRepository exerciseRepository;
        private readonly ExamRepository examRepository;
        private readonly LectureRepository lectureRepository;

        public AssignmentsDialog(PhoenixContext phoenixContext)
            : base(nameof(AssignmentsDialog))
        {
            this.exerciseRepository = new ExerciseRepository(phoenixContext);
            this.examRepository = new ExamRepository(phoenixContext);
            this.lectureRepository = new LectureRepository(phoenixContext);
            this.lectureRepository.Include(l => l.Course);

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Assignments.Homework,
                new WaterfallStep[]
                {
                    HomeworkStepAsync
                }));

            InitialDialogId = WaterfallNames.Actions.Assignments.Homework;
        }

        #region Homework Waterfall Dialog

        private async Task<DialogTurnResult> HomeworkStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var options = stepContext.Options as AssignmentsOptions;
            IQueryable<Exercise> exercises;
            IQueryable<Exam> exams;
            int exercisesNum, examsNum;

            if (options.LectureId.HasValue)     // Students' Search or Teachers' Assignments
            {
                exercises = exerciseRepository.FindForLecture(options.LectureId.Value);
                exercisesNum = exercises?.Count() ?? 0;

                if (!options.Search)            // Find past exams only for Teachers
                {
                    exams = examRepository.FindForLecture(options.LectureId.Value);
                    examsNum = exams?.Count() ?? 0;

                    if (exercisesNum == 0 && examsNum == 0)
                    {
                        await stepContext.Context.SendActivityAsync($"Δε βρέθηκαν εργασίες, ούτε διαγωνίσματα για τις {options.DateToPrepareFor.Value:d/M}.");
                        return await stepContext.EndDialogAsync(null, cancellationToken);
                    }
                }
                else
                {
                    exams = null;
                    examsNum = 0;

                    if (exercisesNum == 0)
                    {
                        await stepContext.Context.SendActivityAsync($"Δε βρέθηκαν εργασίες για τις {options.DateToPrepareFor.Value:d/M}.");
                        return await stepContext.EndDialogAsync(null, cancellationToken);
                    }
                }
            }
            else                                // Students' Assignments
            {
                exercises = exerciseRepository.FindForStudent(options.ActiveUserId, Tense.Future);
                exams = examRepository.FindForStudent(options.ActiveUserId, Tense.Future);

                exercisesNum = exercises?.Count() ?? 0;
                examsNum = exams?.Count() ?? 0;

                if (exercisesNum == 0 && examsNum == 0)
                {
                    await stepContext.Context.SendActivityAsync("Δεν υπάρχουν ακόμα εργασίες για το επόμενο μάθημα.");
                    if (!options.AffiliatedUserId.HasValue)
                        await stepContext.Context.SendActivityAsync("Απόλαυσε τον ελεύθερο χρόνο σου! 😎");

                    return await stepContext.EndDialogAsync(null, cancellationToken);
                }
            }

            string msg = options.LectureId.HasValue ? $"Για τις {options.DateToPrepareFor.Value:d/M} βρέθκαν:" : "Για την επόμενη φορά βρέθηκαν:";
            if (exercisesNum > 0)
                msg += $"\n  •  {exercisesNum} εργασίες";
            if (examsNum > 0)
                msg += $"\n  •  {examsNum} διαγωνίσματα";

            var adaptiveCards = new List<AdaptiveCard>(exercisesNum + examsNum);
            adaptiveCards.AddRange(await this.GetCardsAsync(exercises, options.ActiveUserId));
            adaptiveCards.AddRange(await this.GetCardsAsync(exams));

            var attachments = adaptiveCards.Select(c => new Attachment(contentType: AdaptiveCard.ContentType, content: JObject.FromObject(c)));
            await stepContext.Context.SendActivityAsync(MessageFactory.Attachment(attachments, text: msg));

            return await stepContext.EndDialogAsync(null, cancellationToken);
        }

        private async Task<List<AdaptiveCard>> GetCardsAsync(IQueryable<Exercise> exercises, int activeUserId)
        {
            List<AdaptiveCard> cards = null;

            if ((exercises?.Count() ?? 0) != 0)
            {
                var exerciseGroups = exercises.GroupBy(e => e.LectureId);
                int groupsNum = exerciseGroups.Count();
                cards = new List<AdaptiveCard>(groupsNum);

                foreach (var exerciseGroup in exerciseGroups)
                {
                    var lecture = await lectureRepository.Find(exerciseGroup.Key);

                    var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2))
                    {
                        BackgroundImage = new AdaptiveBackgroundImage(AdaptiveCardsHelper.DarkBackgroundImageUrl)
                    };

                    string title = groupsNum == 1 ? "Εργασία" : "Εργασίες";
                    card.Body.Add(new AdaptiveTextBlockHeaderLight($"{title}: {lecture.Course.NameWithSubcourse}"));
                    card.Body.Add(new AdaptiveTextBlockHeaderLight($"{lecture.StartDateTime:g}"));

                    int i = 1;
                    foreach (var exercise in exerciseGroup)
                    {
                        if (groupsNum > 1)
                            card.Body.Add(new AdaptiveTextBlockHeaderLight($"Εργασία {i++}") { Size = AdaptiveTextSize.Large });
                        if (exercise.Book != null)
                            card.Body.Add(new AdaptiveRichFactSetLight("Βιβλίο ", exercise.Book.Name));
                        if (!string.IsNullOrWhiteSpace(exercise.Page))
                            card.Body.Add(new AdaptiveRichFactSetLight("Σελίδα ", exercise.Page, separator: true));
                        if (lecture.StartDateTime.ToUniversalTime() < DateTimeOffset.UtcNow)
                        {
                            var grade = exerciseRepository.FindGrade(activeUserId, exercise.Id);
                            card.Body.Add(new AdaptiveRichFactSetLight("Βαθμός ", grade == null ? "-" : grade.ToString(), separator: true));
                        }
                        card.Body.Add(new AdaptiveRichFactSetLight("Άσκηση ", exercise.Name, separator: true));
                        card.Body.Add(new AdaptiveRichFactSetLight("Σχόλια ", string.IsNullOrWhiteSpace(exercise.Comments) ? "-" : exercise.Comments, separator: true));
                    }
                    
                    cards.Add(card);
                }
            }

            return cards;
        }

        private async Task<List<AdaptiveCard>> GetCardsAsync(IQueryable<Exam> exams)
        {
            List<AdaptiveCard> cards = null;

            if ((exams?.Count() ?? 0) != 0)
            {
                var examGroups = exams.GroupBy(e => e.LectureId);
                int groupsNum = examGroups.Count();
                cards = new List<AdaptiveCard>(groupsNum);

                foreach (var examGroup in examGroups)
                {
                    var lecture = await lectureRepository.Find(examGroup.Key);

                    var card = new AdaptiveCard(new AdaptiveSchemaVersion(1, 2))
                    {
                        BackgroundImage = new AdaptiveBackgroundImage(AdaptiveCardsHelper.DarkBackgroundImageUrl)
                    };

                    string title = groupsNum == 1 ? "Διαγώνισμα" : "Διαγωνίσματα";
                    card.Body.Add(new AdaptiveTextBlockHeaderLight($"{title}: {lecture.Course.NameWithSubcourse}"));
                    card.Body.Add(new AdaptiveTextBlockHeaderLight($"{lecture.StartDateTime:g}"));

                    int i = 1;
                    foreach (var exam in examGroup)
                    {
                        if (groupsNum > 1)
                            card.Body.Add(new AdaptiveTextBlockHeaderLight($"Διαγώνισμα {i++}") { Size = AdaptiveTextSize.Large });

                        int j = 1;
                        foreach (var material in exam.Material)
                        {
                            if (exam.Material.Count() > 1)
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
