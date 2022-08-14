using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Errors;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Main.Types;
using Phoenix.DataHandle.Repositories;
using Phoenix.DataHandle.Utilities;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class LecturePreparation : PreparationComponent
    {
        private readonly LectureRepository _lectureRepository;

        protected override string AskPrompt => "Με ποια διάλεξη (ώρα μαθήματος) θα ήθελες να συνεχίσουμε;";
        protected override string AskRePrompt => "Παρακαλώ επίλεξε ή πληκτρολόγησε μία από τις παρακάτω (ώρες):";

        public LecturePreparation(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(BotActionPreparation.LectureSelection,
                  userState, convState, userManager, phoenixContext)
        {
            _lectureRepository = new(phoenixContext, nonObviatedOnly: true)
            {
                SearchNonCancelledOnly = true
            };
        }

        protected override async Task<DialogTurnResult> InitializeStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (PreparationComponentOptions)stepCtx.Options;
            _lectureRepository.SearchWithExamsOnly = options.ExamsOnly;

            int[] courseIds = options.CourseIdToPrepareFor.HasValue
                ? new[] { options.CourseIdToPrepareFor.Value }
                : (await FindUserToPrepareForAsync(options)).Courses.Select(c => c.Id).ToArray();

            var dateToPrepareFor = options.DateToPrepareFor?.Date ??
                CalendarExtensions.TimeZoneNow(CData.School.SchoolSetting.TimeZone).Date;

            var lectures = _lectureRepository.Search(courseIds, dateToPrepareFor);

            if (!lectures.Any())
            {
                var tense = options.CourseIdToPrepareFor.HasValue ? Tense.Past : Tense.Anytime;
                var closestDate = _lectureRepository
                    .Search(courseIds, tense, dateToPrepareFor, max: 1)
                    .SingleOrDefault()?
                    .StartDateTime;

                if (!closestDate.HasValue)
                {
                    BotError error;
                    if (options.CourseIdToPrepareFor.HasValue)
                        error = options.ExamsOnly ? BotError.CourseHasNoExams : BotError.CourseHasNoLectures;
                    else
                        error = options.ExamsOnly ? BotError.UserHasNoExams : BotError.UserHasNoLectures;

                    throw new BotException(error);
                }

                lectures = _lectureRepository.Search(courseIds, closestDate.Value.Date);

                var activities = new[]
                {
                    MessageFactory.Text("Δεν υπάρχουν " +
                        (options.ExamsOnly ? "διαγωνίσματα " : "διαλέξεις ") + $"στις {dateToPrepareFor:d/M} " +
                        (options.CourseIdToPrepareFor.HasValue ? "για αυτό το μάθημα." : ".")),
                    MessageFactory.Text($"Βρήκα όμως για την πιο κοντινή στις {closestDate:d/M}:")
                };

                await stepCtx.Context.SendActivitiesAsync(activities, canTkn);
            }

            options.Selectables = GetSelectables(lectures);

            return await stepCtx.NextAsync(null, canTkn);
        }
    }
}
