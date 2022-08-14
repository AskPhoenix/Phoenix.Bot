using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Microsoft.Bot.Schema;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.Dialogs.Helpers;
using Phoenix.Bot.Utilities.Errors;
using Phoenix.Bot.Utilities.Linguistic;
using Phoenix.Bot.Utilities.State.Options.Actions.Preparation;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Main.Types;
using Phoenix.DataHandle.Repositories;
using Phoenix.DataHandle.Utilities;

namespace Phoenix.Bot.Dialogs.Actions.Preparation
{
    public class DatePreparation : PreparationComponent
    {
        private readonly LectureRepository _lectureRepository;

        protected override string AskPrompt => "Επίλεξε μία από τις παρακάτω κοντινές ημερομηνίες ή γράψε κάποια άλλη:";
        protected override string AskRePrompt => "Η επιθυμητή ημερομηνία θα πρέπει να είναι στη μορφή ημέρα/μήνας:";

        public DatePreparation(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(BotActionPreparation.DateSelection,
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

            // Student --> Search, Grades | Teacher --> Assignments, Grades
            var tense = options.CourseIdToPrepareFor.HasValue ? Tense.Past : Tense.Anytime;
            var dates = _lectureRepository
                .Search(courseIds, Tense.Past, DateTimeOffset.UtcNow)
                .Select(l => l.StartDateTime);
            
            options.Selectables = GetSelectables(dates, dateFormat: "d/M");

            if (!options.Selectables.Any())
            {
                BotError error;
                if (options.CourseIdToPrepareFor.HasValue)
                    error = options.ExamsOnly ? BotError.CourseHasNoExams : BotError.CourseHasNoLectures;
                else
                    error = options.ExamsOnly ? BotError.UserHasNoExams : BotError.UserHasNoLectures;

                throw new BotException(error);
            }
           
            if (options.Selectables.Count == 1)
                return await stepCtx.EndDialogAsync(dates.Single(), canTkn);

            return await stepCtx.NextAsync(null, canTkn);
        }

        protected override async Task<DialogTurnResult> AskStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var options = (PreparationComponentOptions)stepCtx.Options;
            var choices = ChoiceFactory.ToChoices(options.Selectables!.Values.ToList());

            var today = CalendarExtensions.TimeZoneNow(CData.School.SchoolSetting.TimeZone);

            for (int i = 0; i < choices.Count; i++)
            {
                var date = CalendarExtensions.ParseExact(choices[i].Value, format: "d/M").Date;

                if (date == today)
                    choices[i].Value = DateLiteral.Today.ToFriendlyString();
                else if (date == today.AddDays(-1.0))
                    choices[i].Value = DateLiteral.Yesterday.ToFriendlyString();
                else if (date == today.AddDays(1.0))
                    choices[i].Value = DateLiteral.Tomorrow.ToFriendlyString();
            }

            return await stepCtx.PromptAsync(
                nameof(DateTimePrompt),
                new PromptOptions
                {
                    Prompt = (Activity)ChoiceFactory.SuggestedAction(choices, text: this.AskPrompt),
                    RetryPrompt = (Activity)ChoiceFactory.SuggestedAction(choices, text: this.AskRePrompt)
                }, canTkn);
        }

        protected override async Task<DialogTurnResult> SelectStepAsync(WaterfallStepContext stepCtx,
            CancellationToken canTkn)
        {
            var msg = stepCtx.Context.Activity.Text;
            var res = (IList<DateTimeResolution>)stepCtx.Result;

            var offset = CalendarExtensions.CalculateTimeZoneOffset(
                CData.School.SchoolSetting.TimeZone, DateTime.UtcNow);

            var resolvedDate = ResolveHelper.ResolveDateTimePromptResult(res, msg).ToOffset(offset);

            return await stepCtx.EndDialogAsync(resolvedDate, canTkn);
        }
    }
}
