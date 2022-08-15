using AdaptiveCards;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Dialogs.Choices;
using Phoenix.Bot.Utilities.Actions;
using Phoenix.Bot.Utilities.AdaptiveCards;
using Phoenix.Bot.Utilities.Dialogs;
using Phoenix.Bot.Utilities.Dialogs.Prompts;
using Phoenix.Bot.Utilities.Dialogs.Prompts.Options;
using Phoenix.Bot.Utilities.State.Options.Actions;
using Phoenix.DataHandle.Identity;
using Phoenix.DataHandle.Main.Models;
using Phoenix.DataHandle.Repositories;
using Phoenix.DataHandle.Utilities;

namespace Phoenix.Bot.Dialogs.Actions
{
    public class ScheduleDialog : StateDialog
    {
        private readonly CourseRepository _courseRepository;
        private readonly LectureRepository _lectureRepository;

        public ScheduleDialog(
            UserState userState,
            ConversationState convState,
            ApplicationUserManager userManager,
            PhoenixContext phoenixContext)
            : base(userState, convState, userManager, phoenixContext, nameof(ScheduleDialog))
        {
            _courseRepository = new(phoenixContext, nonObviatedOnly: true);
            _lectureRepository = new(phoenixContext, nonObviatedOnly: true);

            AddDialog(new UnaccentedChoicePrompt(nameof(UnaccentedChoicePrompt)));
            AddDialog(new DateTimePrompt(nameof(DateTimePrompt)));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Schedule.Weekly,
                new WaterfallStep[]
                {
                    WeeklyStepAsync,
                    OtherDayStepAsync
                }));

            AddDialog(new WaterfallDialog(WaterfallNames.Actions.Schedule.Daily,
                new WaterfallStep[]
                {
                    DailyStepAsync,
                    OtherDayStepAsync,
                }));

            InitialDialogId = WaterfallNames.Actions.Schedule.Weekly;
        }

        protected override Task<DialogTurnResult> OnBeginDialogAsync(DialogContext innerDc, object options,
            CancellationToken canTkn = default)
        {
            var scheduleOptions = (ScheduleOptions)options;

            if (scheduleOptions.Daily)
                InitialDialogId = WaterfallNames.Actions.Schedule.Daily;

            return base.OnBeginDialogAsync(innerDc, options, canTkn);
        }

        #region Weekly Waterfall Dialog

        private async Task<DialogTurnResult> WeeklyStepAsync(WaterfallStepContext steCtx,
            CancellationToken canTkn)
        {
            var options = (ScheduleOptions)steCtx.Options;

            var today = CalendarExtensions.TimeZoneNow(CData.School.SchoolSetting.TimeZone).Date;
            var monday = today.AddDays(-(int)today.DayOfWeek);

            var courseIds = UData.PhoenixUser!.Courses.Select(c => c.Id).ToArray();

            var card = new AdaptivePhoenixCard(new AdaptiveTextBlockHeaderLight[]
            {
                new("Εβδομαδιαίο πρόγραμμα"),
                new($"{monday:d/M} έως {monday.AddDays(6):d/M}")
            });

            bool weekHasLectures = false;

            for (int i = 0; i < 6; i++)
            {
                var nextDay = monday.AddDays(i).Date;
                var dayLectures = _lectureRepository.Search(courseIds, nextDay);

                if (dayLectures.Any())
                {
                    weekHasLectures = true;

                    card.Body.Add(new AdaptiveTextBlockHeaderLight(nextDay.ToString("dddd")));

                    foreach (var lec in dayLectures)
                        card.Body.AddRange(new AdaptiveRichFactSetLight[]
                        {
                            new("Μάθημα ", lec.Course.GetNameWithSubcourse()),
                            new("Ώρες ", $"{lec.StartDateTime:t} - {lec.EndDateTime:t}", separator: true),
                            new()
                        });
                }
            }

            if (weekHasLectures)
            {
                await steCtx.Context.SendActivityAsync("Παρακάτω θα βρεις το πρόγραμμα της τρέχουσας εβδομάδας:");
                await steCtx.Context.SendActivityAsync(card.ToActivity());
            }
            else
                await steCtx.Context.SendActivityAsync($"Δεν έχουν προγραμματιστεί μαθήματα για την εβδομάδα {monday:d/M} έως {monday.AddDays(6):d/M}! 😎");

            return await steCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να δεις το πρόγραμμα για άλλη ημέρα;"),
                canTkn);
        }

        private async Task<DialogTurnResult> OtherDayStepAsync(WaterfallStepContext steCtx,
            CancellationToken canTkn)
        {
            var foundChoice = (FoundChoice)steCtx.Result;

            if (foundChoice.Index == 0)
                return await steCtx.EndDialogAsync(BotAction.ScheduleDay, canTkn);

            await steCtx.Context.SendActivityAsync("OK 😊");

            return await steCtx.EndDialogAsync(null, canTkn);
        }

        #endregion

        #region Daily Waterfall Dialog

        private async Task<DialogTurnResult> DailyStepAsync(WaterfallStepContext steCtx,
            CancellationToken canTkn)
        {
            var scheduleOptions = (ScheduleOptions)steCtx.Options;

            var date = scheduleOptions.DateToPrepareFor!.Value.Date;
            var courseIds = UData.PhoenixUser!.Courses.Select(c => c.Id).ToArray();

            var lectures = _lectureRepository.Search(courseIds, date);

            var now = CalendarExtensions.TimeZoneNow(CData.School.SchoolSetting.TimeZone);
            int dayOffset = (date - now.Date).Days;

            string dayName = dayOffset switch
            {
                var o when o <= -2 && o > -7 => $"{(date.DayOfWeek == DayOfWeek.Saturday ? "το προηγούμενο" : "την προηγούμενη")} {date:dddd}",
                -1 => "χθες",
                0 => "σήμερα",
                1 => "αύριο",
                var o when o >= 2 && o < 7 => $"{(date.DayOfWeek == DayOfWeek.Saturday ? "το επόμενο" : "την επόμενη")} {date:dddd}",
                _ => $"τις {date:d/M}"
            };

            if (lectures.Any())
            {
                var card = new AdaptivePhoenixCard();
                card.Body.Add(new AdaptiveTextBlockHeaderLight($"{date:D}"));

                foreach (var lec in lectures)
                {
                    card.Body.AddRange(new AdaptiveElement[]
                    {
                        new AdaptiveTextBlockHeaderLight(lec.Course.GetNameWithSubcourse()),
                        new AdaptiveRichFactSetLight("Ώρες ", $"{lec.StartDateTime:t} - {lec.EndDateTime:t}")
                    });

                    if (lec.Classroom != null)
                        card.Body.Add(new AdaptiveRichFactSetLight("Αίθουσα ", lec.Classroom.Name, separator: true));

                    card.Body.AddRange(new AdaptiveRichFactSetLight[]
                    {
                        new("Κατάσταση ", lec.IsCancelled ? "Ακυρώθηκε" : "Προγραμματισμένη", separator: true),
                        new("Σχόλια ", string.IsNullOrEmpty(lec.Comments) ? "-" : lec.Comments, separator: true)
                    });
                }

                await steCtx.Context.SendActivityAsync($"Ορίστε το πρόγραμμα για {dayName}:");

                await steCtx.Context.SendActivityAsync(card.ToActivity());
            }
            else
                await steCtx.Context.SendActivityAsync($"Δεν έχουν προγραμματιστεί μαθήματα για {dayName}! 😎");

            return await steCtx.PromptAsync(
                nameof(UnaccentedChoicePrompt),
                new YesNoPromptOptions("Θα ήθελες να δεις το πρόγραμμα για άλλη ημέρα;"),
                canTkn);
        }

        #endregion
    }
}
